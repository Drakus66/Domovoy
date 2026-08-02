#!/usr/bin/env node
// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

/**
 * Генератор бандла топологии (Эпик 3K).
 *
 * Единственный источник истины по топологии — корневой `docker-compose.yml`, который
 * остаётся файлом для локальной разработки и не меняет формата. Боевой compose не
 * хранится в репозитории отдельной копией (она бы разъехалась), а собирается отсюда:
 *
 *   • у компонентов из build/components.json удаляется `build:` и подменяется `image:`
 *     на адрес в реестре с подвижным тегом канала;
 *   • вырезаются блоки, помеченные `# >>> dev-only` … `# <<< dev-only`;
 *   • рядом кладутся .env.template, manifest.json и конфиги-ассеты из манифеста.
 *
 * Трансформация построчная и намеренно без YAML-парсера: node в проекте уже есть
 * (WebUI + CI), тянуть зависимость ради восьми подстановок незачем. Гарантию даёт не
 * парсер, а валидация результата — CI прогоняет `docker compose config` на выходе.
 *
 * Использование:
 *   node build/tools/gen-topology.mjs --out build/topology/dist [--channel release]
 */

import { readFileSync, writeFileSync, mkdirSync, copyFileSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

function arg(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i !== -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

const outDir = resolve(repoRoot, arg('out', 'build/topology/dist'));
const channelDefault = arg('channel', 'release');

const spec = JSON.parse(readFileSync(join(repoRoot, 'build/components.json'), 'utf8'));
const manifest = JSON.parse(readFileSync(join(repoRoot, 'build/topology/manifest.json'), 'utf8'));

/** compose-имя сервиса → адрес образа в реестре. */
const imageByService = new Map(
  spec.components.map((c) => [c.container, `${spec.registry}/${spec.imagePrefix}${c.name}`]),
);

const source = readFileSync(join(repoRoot, 'docker-compose.yml'), 'utf8');
const lines = source.split(/\r?\n/);

const out = [];
let currentService = null; // compose-имя сервиса, чей блок мы сейчас разбираем
let inServices = false;
let skipIndent = null; // отступ вырезаемого вложенного блока (build:)
let devOnly = false;
const replaced = new Set();

const indentOf = (l) => l.length - l.trimStart().length;

for (const line of lines) {
  // --- маркеры блоков, нужных только локальной разработке ---
  if (line.includes('# >>> dev-only')) { devOnly = true; continue; }
  if (line.includes('# <<< dev-only')) { devOnly = false; continue; }
  if (devOnly) continue;

  // --- продолжение вырезаемого блока build: ---
  if (skipIndent !== null) {
    if (line.trim() === '' || indentOf(line) > skipIndent) continue;
    skipIndent = null;
  }

  // --- отслеживание текущего сервиса ---
  if (/^services:\s*$/.test(line)) { inServices = true; out.push(line); continue; }
  if (/^[a-zA-Z]/.test(line)) inServices = false; // вышли на верхний уровень (volumes:, networks:)

  if (inServices) {
    const m = line.match(/^ {2}([A-Za-z0-9._-]+):\s*$/);
    if (m) currentService = m[1];
  }

  const isOurs = currentService !== null && imageByService.has(currentService);

  if (isOurs) {
    // build: со всем вложенным содержимым — в боевой установке ничего не собирается
    if (/^\s*build:\s*$/.test(line)) { skipIndent = indentOf(line); continue; }

    // image: → адрес в реестре с подвижным тегом канала
    if (/^\s*image:\s*/.test(line)) {
      const indent = ' '.repeat(indentOf(line));
      out.push(`${indent}image: ${imageByService.get(currentService)}:\${DOMOVOY_CHANNEL:-${channelDefault}}`);
      replaced.add(currentService);
      continue;
    }
  }

  out.push(line);
}

const missing = [...imageByService.keys()].filter((s) => !replaced.has(s));
if (missing.length) {
  console.error(
    `gen-topology: в docker-compose.yml не найден image: для сервисов: ${missing.join(', ')}.\n` +
    'Каждый компонент из build/components.json обязан быть сервисом compose с полем image:.',
  );
  process.exit(1);
}

const header = [
  '# ============================================================================',
  '# СГЕНЕРИРОВАННЫЙ ФАЙЛ — НЕ РЕДАКТИРОВАТЬ.',
  '# Источник: docker-compose.yml + build/components.json (build/tools/gen-topology.mjs).',
  '#',
  '# Это боевая топология: образы берутся из реестра, ничего не собирается на месте.',
  '# Локальные настройки живут в .env и docker-compose.override.yml — они принадлежат',
  '# владельцу установки и обновлением не изменяются.',
  '#',
  '# Запускать ТОЛЬКО с явным каталогом проекта, иначе относительные пути (./data, ./plugins)',
  '# уедут внутрь каталога релиза:',
  '#   docker compose --project-directory /opt/domovoy \\',
  '#     -f releases/current/docker-compose.yml -f docker-compose.override.yml \\',
  '#     -f state/pinned.yml --env-file .env up -d',
  '# ============================================================================',
  '',
].join('\n');

mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'docker-compose.yml'), header + out.join('\n'), 'utf8');
copyFileSync(join(repoRoot, 'build/topology/.env.template'), join(outDir, '.env.template'));
copyFileSync(join(repoRoot, 'build/topology/manifest.json'), join(outDir, 'manifest.json'));

// Конфиги, которые compose монтирует с хоста: они часть топологии, а не пользовательские данные.
// TopologyManager раскладывает их в корень установки при применении.
for (const asset of manifest.assets ?? []) {
  const src = join(repoRoot, asset);
  if (!existsSync(src)) {
    console.error(`gen-topology: ассет из manifest.json не найден: ${asset}`);
    process.exit(1);
  }
  const dst = join(outDir, 'assets', asset);
  mkdirSync(dirname(dst), { recursive: true });
  copyFileSync(src, dst);
}

console.log(
  `gen-topology: топология v${manifest.version} собрана в ${outDir} ` +
  `(${replaced.size} компонентов, ${(manifest.assets ?? []).length} ассетов)`,
);
