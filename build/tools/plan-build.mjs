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
 * Есть ли у компонента образ под тегом канала.
 *
 * Селективная сборка исходит из того, что непересобранный компонент уже лежит в реестре. Это верно
 * ровно до первого исключения: на пустом реестре или после упавшей сборки одного компонента
 * (fail-fast: false) в канале остаётся дыра, а следующий коммит его не затронет и не пересоберёт.
 * Дыра при этом молчаливая: сборка зелёная, а набор в канале неполный, и решатель на стороне дома
 * просто не увидит компонент. Поэтому отсутствующее в канале достраивается независимо от диффа.
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

      const head = await fetch(`https://ghcr.io/v2/${repository}/manifests/${channel}`, {
        method: 'HEAD',
        headers: {
          Authorization: `Bearer ${token}`,
          Accept: 'application/vnd.oci.image.index.v1+json,'
            + 'application/vnd.oci.image.manifest.v1+json,'
            + 'application/vnd.docker.distribution.manifest.v2+json,'
            + 'application/vnd.docker.distribution.manifest.list.v2+json',
        },
      });

      if (!head.ok) missing.push(c.name);
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

const topology = topologyAffected
  ? {
      image: topologyImageOf(spec),
      version: `${spec.topology.version}.${runNumber}`,
      deps: JSON.stringify({
        component: 'topology',
        version: spec.topology.version,
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
