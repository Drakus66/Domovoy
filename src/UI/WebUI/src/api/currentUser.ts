// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

/**
 * Self-declared local user for attribution (Epic 2G tail): "who of the household is at this browser".
 * NOT authentication — Phase 3 will replace this with a real identity. When set, every command carries
 * the `X-Domovoy-User` header (see client.ts) so the event-log records which user acted.
 */
const KEY = 'domovoy.userId';

export const getCurrentUserId = (): string | null => {
  try {
    return localStorage.getItem(KEY);
  } catch {
    return null;
  }
};

export const setCurrentUserId = (id: string | null): void => {
  try {
    if (id) localStorage.setItem(KEY, id);
    else localStorage.removeItem(KEY);
  } catch {
    // Storage unavailable (private mode) — attribution simply stays anonymous.
  }
};
