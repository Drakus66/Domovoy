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

const include = spec.components.filter(affected).map((c) => {
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

const topologyAffected = buildAll || changed.some((f) => matchesAny(spec.topology.paths, f));
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
