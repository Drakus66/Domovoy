#!/usr/bin/env node
// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

/**
 * Гейт совместимости набора (Эпик 3K, job `validate`).
 *
 * Роняет сборку, если объявленный в build/components.json набор внутренне несовместим:
 * интерфейс без провайдера, requires вне диапазона провайдера, расхождение по версии шины.
 * Смысл гейта — не дать несовместимости доехать до боевого дома.
 *
 *   node build/tools/validate-components.mjs
 */

import { loadSpec, validateSpec } from './spec.mjs';

const spec = loadSpec();
const problems = validateSpec(spec);

if (problems.length) {
  console.error('Набор компонентов несовместим:\n');
  for (const p of problems) console.error(`  • ${p}`);
  console.error(
    '\nПоправьте build/components.json — правила в docs/architecture/coding_standards_ru.md,\n' +
    'раздел «Контракты и совместимость».',
  );
  process.exit(1);
}

console.log(`Набор согласован: ${spec.components.length} компонентов, топология v${spec.topology.version}.`);
