// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

/**
 * Общие утилиты для инструментов сборки (Эпик 3K): чтение build/components.json,
 * сопоставление путей и статическая проверка совместимости объявленного набора.
 */

import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

export function loadSpec() {
  return JSON.parse(readFileSync(join(repoRoot, 'build/components.json'), 'utf8'));
}

export function loadTopologyManifest() {
  return JSON.parse(readFileSync(join(repoRoot, 'build/topology/manifest.json'), 'utf8'));
}

/**
 * Сопоставление пути с паттерном из спецификации. Поддерживается ровно то, что нужно:
 * префикс `dir/**` и точное совпадение файла. Полноценный glob здесь был бы лишним —
 * паттерны пишем мы сами, и они намеренно простые.
 */
export function matchesPath(pattern, file) {
  const normalized = file.replace(/\\/g, '/');
  if (pattern.endsWith('/**')) return normalized.startsWith(pattern.slice(0, -2));
  return normalized === pattern;
}

export function matchesAny(patterns, file) {
  return (patterns ?? []).some((p) => matchesPath(p, file));
}

/** Компонент → образ в реестре (без тега). */
export function imageOf(spec, component) {
  return `${spec.registry}/${spec.imagePrefix}${component.name}`;
}

export function topologyImageOf(spec) {
  return `${spec.registry}/${spec.imagePrefix}${spec.topology.image}`;
}

/**
 * Статическая проверка совместимости ОБЪЯВЛЕННОГО набора.
 *
 * Это не полный решатель — тот живёт в Domovoy.Updater и умеет подбирать версии из канала,
 * потому что нужен в рантайме. Здесь проверяется более узкое и куда более полезное для CI
 * утверждение: набор, который мы сейчас публикуем, внутренне непротиворечив. Если это не так,
 * несовместимость доедет до боевого дома — поэтому job валидации обязан упасть.
 *
 * @returns {string[]} список проблем; пустой массив = набор согласован.
 */
export function validateSpec(spec) {
  const problems = [];
  const components = spec.components;

  // 1. У каждого объявленного интерфейса должен быть ровно один провайдер.
  const providers = new Map(); // интерфейс → [компоненты]
  for (const c of components) {
    for (const iface of Object.keys(c.provides ?? {})) {
      if (!providers.has(iface)) providers.set(iface, []);
      providers.get(iface).push(c.name);
    }
  }
  providers.set('topology', ['<topology-bundle>']);

  for (const [iface, owners] of providers) {
    if (owners.length > 1) {
      problems.push(
        `Интерфейс '${iface}' предоставляют несколько компонентов: ${owners.join(', ')}. ` +
        'У интерфейса должен быть ровно один провайдер — иначе решатель не знает, кого обновлять.',
      );
    }
  }

  // 2. Каждое requires удовлетворяется провайдером: minCompat <= requires <= version.
  for (const c of components) {
    for (const [iface, required] of Object.entries(c.requires ?? {})) {
      if (iface === 'topology') {
        const { version, minCompat } = spec.topology;
        if (required < minCompat || required > version) {
          problems.push(
            `${c.name} требует topology:${required}, но бандл поддерживает ` +
            `[${minCompat}..${version}].`,
          );
        }
        continue;
      }

      const owner = components.find((p) => (p.provides ?? {})[iface]);
      if (!owner) {
        problems.push(`${c.name} требует интерфейс '${iface}', которого никто не предоставляет.`);
        continue;
      }
      const p = owner.provides[iface];
      if (required < p.minCompat || required > p.version) {
        problems.push(
          `${c.name} требует ${iface}:${required}, но ${owner.name} поддерживает ` +
          `[${p.minCompat}..${p.version}].`,
        );
      }
    }
  }

  // 3. Шина: набор совместим, когда max(understands) <= min(speaks).
  const onBus = components.filter((c) => c.bus);
  if (onBus.length) {
    const maxUnderstands = Math.max(...onBus.map((c) => c.bus.understands));
    const minSpeaks = Math.min(...onBus.map((c) => c.bus.speaks));
    if (maxUnderstands > minSpeaks) {
      const laggards = onBus.filter((c) => c.bus.speaks < maxUnderstands).map((c) => c.name);
      problems.push(
        `Набор несовместим по шине: кто-то понимает только с версии ${maxUnderstands}, ` +
        `а говорят на ${minSpeaks}. Отстают: ${laggards.join(', ')}.`,
      );
    }
    for (const c of onBus) {
      if (c.bus.understands > c.bus.speaks) {
        problems.push(`${c.name}: bus.understands (${c.bus.understands}) больше bus.speaks (${c.bus.speaks}).`);
      }
    }
  }

  // 4. Провайдер не может поддерживать диапазон «наоборот».
  for (const c of components) {
    for (const [iface, p] of Object.entries(c.provides ?? {})) {
      if (p.minCompat > p.version) {
        problems.push(`${c.name}: provides.${iface}.minCompat (${p.minCompat}) больше version (${p.version}).`);
      }
    }
  }

  return problems;
}
