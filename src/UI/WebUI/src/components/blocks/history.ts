// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

/**
 * A tiny, generic undo/redo history reducer (roadmap Epic 1E — the flow editor gets undo/redo, which no graph
 * library ships built-in). Kept pure and framework-free so the branching-stack logic is unit-testable; the
 * component drives it with `useReducer` and treats `present` as its editable document.
 */

export interface History<T> {
  past: T[];
  present: T;
  future: T[];
}

export type HistoryAction<T> =
  | { type: 'set'; next: T } // push a new present, dropping the redo branch
  | { type: 'reset'; present: T } // replace present and clear all history (e.g. upstream reload)
  | { type: 'undo' }
  | { type: 'redo' };

/** Upper bound on retained undo steps — keeps a long editing session from growing memory without bound. */
export const HISTORY_LIMIT = 50;

export function historyReducer<T>(state: History<T>, action: HistoryAction<T>): History<T> {
  switch (action.type) {
    case 'set': {
      if (Object.is(action.next, state.present)) return state;
      const past = [...state.past, state.present];
      // Drop the oldest step once we exceed the limit.
      return { past: past.length > HISTORY_LIMIT ? past.slice(past.length - HISTORY_LIMIT) : past, present: action.next, future: [] };
    }
    case 'reset':
      return { past: [], present: action.present, future: [] };
    case 'undo': {
      if (state.past.length === 0) return state;
      const prev = state.past[state.past.length - 1];
      return { past: state.past.slice(0, -1), present: prev, future: [state.present, ...state.future] };
    }
    case 'redo': {
      if (state.future.length === 0) return state;
      const [next, ...rest] = state.future;
      return { past: [...state.past, state.present], present: next, future: rest };
    }
    default:
      return state;
  }
}

export const initHistory = <T>(present: T): History<T> => ({ past: [], present, future: [] });
export const canUndo = <T>(h: History<T>): boolean => h.past.length > 0;
export const canRedo = <T>(h: History<T>): boolean => h.future.length > 0;
