#!/usr/bin/env node
// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

/**
 * Планировщик сборки (Эпик 3K, job `plan`).
 *
 * Определяет, какие компоненты затронул диапазон коммитов, и выдаёт матрицу для job `images`.
 * Смысл — не пересобирать восемь образов из-за правки одного сервиса: непересобранный
 * компонент просто остаётся в реестре со своей прежней версией, а подвижный тег канала
 * продолжает указывать на старый образ.
 *
 * Общие пути (src/Common/**, Directory.*.props, Domovoy.sln) затрагивают все .NET-компоненты:
 * изменение контрактов обязано пересобрать всех, кто их в себе несёт.
 *
 *   node build/tools/plan-build.mjs --changed <файл-со-списком> --channel dev --run 57 --sha ab12cd3
 *   node build/tools/plan-build.mjs --all --channel release --run 57 --sha ab12cd3
 *
 * Выводит JSON: { matrix: {include: [...]}, topology: {...}|null, any: bool }
 */

import { readFileSync, appendFileSync } from 'node:fs';
import { loadSpec, loadTopologyManifest, matchesAny, imageOf, topologyImageOf } from './spec.mjs';

function arg(name, fallback = undefined) {
  const i = process.argv.indexOf(`--${name}`);
  return i !== -1 && process.argv[i + 1] && !process.argv[i + 1].startsWith('--')
    ? process.argv[i + 1]
    : fallback;
}
const flag = (name) => process.argv.includes(`--${name}`);

const spec = loadSpec();
const topologyManifest = loadTopologyManifest();

const channel = arg('channel', 'dev');
const runNumber = arg('run', '0');
const sha = (arg('sha', 'local')).slice(0, 7);

const buildAll = flag('all');
const changedFile = arg('changed');
const changed = buildAll
  ? []
  : (changedFile ? readFileSync(changedFile, 'utf8') : '')
      .split(/\r?\n/)
      .map((s) => s.trim())
      .filter(Boolean);

/** Полная версия компонента: MAJOR.MINOR из спецификации + PATCH = номер прогона CI. */
function versionOf(component) {
  const base = `${component.version}.${runNumber}`;
  // ВАЖНО: формат тега — часть контракта с домом. Служба обновлений отбирает теги регуляркой
  // `^\d+\.\d+\.\d+(-dev)?$` и по суффиксу отделяет канал (RegistryClient.IsChannelVersionTag).
  // Тег, не попавший под неё, для дома не существует — компонент молча пропадает из канала.
  // develop → пре-релиз: канал виден прямо в версии, и такой тег никогда не спутать с релизным.
  const semver = channel === 'release' ? base : `${base}-dev`;
  // `+sha` запрещён в docker-теге, поэтому в теге его нет, а в InformationalVersion и метке — есть.
  return { tagVersion: semver, informational: `${semver}+${sha}` };
}

function affected(component) {
  if (buildAll) return true;
  if (changed.some((f) => matchesAny(component.paths, f))) return true;
  // Общие пути — только для .NET: WebUI не собирает Domovoy.Contracts в себя.
  if (component.runtime === 'dotnet' && changed.some((f) => matchesAny(spec.sharedPaths, f))) return true;
  return false;
}

/**
 * Формат версионного тега — ровно тот, по которому дом отбирает версии
 * (`RegistryClient.IsChannelVersionTag`). Дублирование намеренное: это граница между сборкой на
 * Node и службой на .NET, и держит её не общий код, а тест `ReleaseTagFormatTests`.
 */
const VERSION_TAG = /^\d+\.\d+\.\d+(-dev)?$/;
const wantsDevSuffix = channel !== 'release';

/**
 * Видит ли дом компонент в этом канале.
 *
 * Селективная сборка исходит из того, что непересобранный компонент уже лежит в реестре. Это верно
 * ровно до первого исключения: на пустом реестре или после упавшей сборки одного компонента
 * (fail-fast: false) в канале остаётся дыра, а следующий коммит его не затронет и не пересоберёт.
 * Дыра при этом молчаливая: сборка зелёная, а набор в канале неполный, и решатель на стороне дома
 * просто не увидит компонент. Поэтому отсутствующее в канале достраивается независимо от диффа.
 *
 * ВАЖНО: наличие проверяется теми же глазами, что у дома, — по версионным тегам канала, а не по
 * подвижному тегу `dev`/`release`. Подвижный тег есть всегда, как только пакет опубликовали хоть раз,
 * поэтому проверка по нему считала дыру закрытой, пока бандл топологии лежал под тегом `3.4`,
 * невидимым для службы обновлений: дом отказывался обновляться, а достраивание молчало.
 *
 * Тот же анонимный pull-токен, что использует служба обновлений: пакеты публичные.
 */
async function missingInChannel(components) {
  const missing = [];

  for (const c of components) {
    const repository = `${spec.registry.split('/').slice(1).join('/')}/${spec.imagePrefix}${c.name}`;
    try {
      const auth = await fetch(
        `https://ghcr.io/token?scope=${encodeURIComponent(`repository:${repository}:pull`)}&service=ghcr.io`,
      );
      const { token } = await auth.json();

      const response = await fetch(`https://ghcr.io/v2/${repository}/tags/list`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' },
      });

      // 404 — пакета нет вовсе (первое заполнение реестра или удалили руками).
      if (!response.ok) {
        missing.push(c.name);
        continue;
      }

      const { tags } = await response.json();
      const visible = (tags ?? []).filter(
        (t) => VERSION_TAG.test(t) && t.endsWith('-dev') === wantsDevSuffix,
      );

      if (visible.length === 0) missing.push(c.name);
    } catch (error) {
      // Реестр недоступен — достраивать вслепую хуже, чем не достраивать: собрали бы всё на каждом
      // пуше при любом сетевом сбое. Пропускаем и полагаемся на дифф.
      console.error(`plan: не удалось проверить '${c.name}' в канале — ${error.message}`);
    }
  }

  return missing;
}

// Затронутое диффом + то, чего в канале физически нет (первое заполнение реестра, упавшая
// сборка компонента, ручное удаление пакета). Проверка отключается флагом --no-ensure-present.
const selected = spec.components.filter(affected);

if (!buildAll && !flag('no-ensure-present')) {
  const candidates = spec.components.filter((c) => !selected.includes(c));
  const missing = await missingInChannel(candidates);

  for (const name of missing) {
    const component = spec.components.find((c) => c.name === name);
    if (component) selected.push(component);
  }

  if (missing.length) {
    console.error(`plan: достраиваем отсутствующее в канале '${channel}': ${missing.join(', ')}`);
  }
}

const include = selected.map((c) => {
  const { tagVersion, informational } = versionOf(c);
  return {
    name: c.name,
    context: c.context,
    // build-push-action резолвит `file` от корня рабочей копии, а не от контекста сборки,
    // поэтому склеиваем здесь — в YAML это превратилось бы в нечитаемое выражение.
    file: c.context === '.' ? c.dockerfile : `${c.context}/${c.dockerfile}`,
    runtime: c.runtime,
    image: imageOf(spec, c),
    version: tagVersion,
    informational,
    // Метка ru.domovoy.deps — то, по чему решатель в рантайме строит план обновления.
    deps: JSON.stringify({
      component: c.name,
      container: c.container,
      version: tagVersion,
      channel,
      bus: c.bus,
      provides: c.provides ?? {},
      requires: c.requires ?? {},
    }),
  };
});

// Бандл топологии — по тем же правилам: затронут диффом либо отсутствует в канале. Без него дом
// не сможет обновиться вообще: именно он несёт compose, по которому поднимается вся установка.
const topologyMissing = !buildAll && !flag('no-ensure-present')
  && (await missingInChannel([{ name: spec.topology.image }])).length > 0;

const topologyAffected = buildAll
  || changed.some((f) => matchesAny(spec.topology.paths, f))
  || topologyMissing;

if (topologyMissing) console.error(`plan: бандла топологии нет в канале '${channel}' — собираем`);

// Бандл версионируется по тем же правилам, что и образы: MAJOR = версия топологии, MINOR = 0,
// PATCH = номер прогона, пре-релизный суффикс — из канала. Это не косметика: дом отбирает теги
// одной регуляркой на все пакеты, а `version` в метке читается в строковое поле (ComponentDeps.Version),
// поэтому число вместо строки делает метку неразбираемой, а бандл — невидимым для решателя.
const topologyVersion = versionOf({ version: `${spec.topology.version}.0` });

const topology = topologyAffected
  ? {
      image: topologyImageOf(spec),
      version: topologyVersion.tagVersion,
      deps: JSON.stringify({
        component: 'topology',
        version: topologyVersion.tagVersion,
        channel,
        provides: { topology: { version: spec.topology.version, minCompat: spec.topology.minCompat } },
        assets: topologyManifest.assets ?? [],
      }),
    }
  : null;

const result = {
  matrix: { include },
  topology,
  any: include.length > 0 || topology !== null,
  channel,
};

// В GitHub Actions пишем в $GITHUB_OUTPUT, локально — в stdout.
const out = process.env.GITHUB_OUTPUT;
if (out) {
  appendFileSync(out, `matrix=${JSON.stringify(result.matrix)}\n`);
  appendFileSync(out, `topology=${topology ? JSON.stringify(topology) : ''}\n`);
  appendFileSync(out, `has_images=${include.length > 0}\n`);
  appendFileSync(out, `has_topology=${topology !== null}\n`);
}

console.log(JSON.stringify(result, null, 2));
console.error(
  `plan: канал ${channel}, компонентов к сборке ${include.length}/${spec.components.length}` +
  `${topology ? ', + бандл топологии' : ''}`,
);
