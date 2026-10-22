// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Human-readable labels for the operator/choice codes that used to leak into the UI as bare strings
// ("eq", "gt", "and", "rising", …). One shared vocabulary because the same codes appear across
// automation/flow rules AND control-block options (a comparator's `op`, a PID's `preset`). Every lookup
// falls back to the raw code, so an unknown choice from a new block type degrades gracefully.

import type { TFunction } from 'i18next';

/** Math symbols for the numeric comparison operators — language-independent, for compact summaries. */
const COMPARE_SYMBOLS: Record<string, string> = {
  eq: '=', ne: '≠', gt: '>', lt: '<', gte: '≥', lte: '≤',
};

/**
 * Compact operator for an inline rule/flow summary — e.g. "temperature > 25": a math symbol for the
 * numeric comparisons, the localized word for "changed", and the raw code as a last resort.
 */
export function operatorSummary(t: TFunction, op?: string | null): string {
  if (!op || op === 'eq') return '=';
  if (op === 'changed') return t('operators:value.changed');
  return COMPARE_SYMBOLS[op] ?? op;
}

/**
 * Full human label for a choice code — comparison operators, boolean/aggregate ops, edge types, PID
 * presets, etc. — for dropdown menu items. Falls back to the raw code for anything not in the dictionary.
 */
export function optionValueLabel(t: TFunction, code: string): string {
  return t(`operators:value.${code}`, code);
}

/** Friendly label for a block option's own name ("op" → "Оператор"); falls back to the raw name. */
export function optionNameLabel(t: TFunction, name: string): string {
  return t(`operators:name.${name}`, name);
}
