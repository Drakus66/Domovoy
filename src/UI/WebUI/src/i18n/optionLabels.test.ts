// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import i18n from 'i18next';
import { operatorSummary, optionValueLabel, optionNameLabel } from './optionLabels';

// The test i18n init (src/test/setup.ts) loads the real ru locale JSON, incl. the operators namespace,
// so these assert the strings a user actually sees — the whole point of this change was to kill "eq"/"gt".
describe('optionLabels', () => {
  const t = i18n.t.bind(i18n);

  it('renders comparison operators as math symbols in compact summaries', () => {
    expect(operatorSummary(t, 'eq')).toBe('=');
    expect(operatorSummary(t, 'ne')).toBe('≠');
    expect(operatorSummary(t, 'gt')).toBe('>');
    expect(operatorSummary(t, 'lt')).toBe('<');
    expect(operatorSummary(t, 'gte')).toBe('≥');
    expect(operatorSummary(t, 'lte')).toBe('≤');
  });

  it('defaults a missing operator to "="', () => {
    expect(operatorSummary(t, undefined)).toBe('=');
    expect(operatorSummary(t, null)).toBe('=');
  });

  it('renders "changed" as a localized word, not a symbol', () => {
    expect(operatorSummary(t, 'changed')).toBe('изменилось');
  });

  it('gives dropdown labels a readable word for every known choice code', () => {
    expect(optionValueLabel(t, 'gt')).toBe('больше (>)');
    expect(optionValueLabel(t, 'and')).toContain('И');
    expect(optionValueLabel(t, 'avg')).toBe('среднее');
    expect(optionValueLabel(t, 'rising')).toContain('нарастающий');
    expect(optionValueLabel(t, 'balanced')).toBe('сбалансированный');
  });

  it('falls back to the raw code for an unknown choice (new block type degrades gracefully)', () => {
    expect(optionValueLabel(t, 'some_new_code')).toBe('some_new_code');
    expect(optionNameLabel(t, 'some_new_option')).toBe('some_new_option');
  });

  it('gives a block option name a friendly label', () => {
    expect(optionNameLabel(t, 'op')).toBe('Оператор');
    expect(optionNameLabel(t, 'preset')).toBe('Профиль настройки');
  });
});
