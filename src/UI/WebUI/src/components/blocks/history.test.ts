// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import { historyReducer, initHistory, canUndo, canRedo, HISTORY_LIMIT, type History } from './history';

const drive = <T>(start: History<T>, actions: Parameters<typeof historyReducer<T>>[1][]): History<T> =>
  actions.reduce((s, a) => historyReducer(s, a), start);

describe('history reducer', () => {
  it('commits, undoes, and redoes', () => {
    let h = initHistory('a');
    h = historyReducer(h, { type: 'set', next: 'b' });
    h = historyReducer(h, { type: 'set', next: 'c' });
    expect(h.present).toBe('c');
    expect(canUndo(h)).toBe(true);
    expect(canRedo(h)).toBe(false);

    h = historyReducer(h, { type: 'undo' });
    expect(h.present).toBe('b');
    h = historyReducer(h, { type: 'undo' });
    expect(h.present).toBe('a');
    expect(canUndo(h)).toBe(false);

    h = historyReducer(h, { type: 'redo' });
    expect(h.present).toBe('b');
    expect(canRedo(h)).toBe(true);
  });

  it('a new commit after undo drops the redo branch', () => {
    let h = drive(initHistory('a'), [{ type: 'set', next: 'b' }, { type: 'set', next: 'c' }, { type: 'undo' }]);
    expect(h.present).toBe('b');
    expect(canRedo(h)).toBe(true);
    h = historyReducer(h, { type: 'set', next: 'x' });
    expect(h.present).toBe('x');
    expect(canRedo(h)).toBe(false); // 'c' is unreachable now
  });

  it('undo/redo at the ends are no-ops (same reference)', () => {
    const h = initHistory('a');
    expect(historyReducer(h, { type: 'undo' })).toBe(h);
    expect(historyReducer(h, { type: 'redo' })).toBe(h);
  });

  it('committing the identical present is a no-op', () => {
    const h = historyReducer(initHistory('a'), { type: 'set', next: 'b' });
    expect(historyReducer(h, { type: 'set', next: 'b' })).toBe(h);
  });

  it('reset clears both stacks', () => {
    const h = drive(initHistory('a'), [{ type: 'set', next: 'b' }, { type: 'set', next: 'c' }]);
    const reset = historyReducer(h, { type: 'reset', present: 'z' });
    expect(reset).toEqual({ past: [], present: 'z', future: [] });
  });

  it('caps the undo depth at HISTORY_LIMIT', () => {
    let h = initHistory(0);
    for (let i = 1; i <= HISTORY_LIMIT + 20; i++) h = historyReducer(h, { type: 'set', next: i });
    expect(h.past.length).toBe(HISTORY_LIMIT);
    // Oldest steps dropped: the earliest retained past entry is not the original 0.
    expect(h.past[0]).toBeGreaterThan(0);
  });
});
