// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Theme selection store for WebUI.
// Persists the chosen theme id in localStorage; light/dark mode is handled
// separately by MUI's color-scheme engine (see ColorModeToggle).

import { create } from 'zustand';
import { DEFAULT_THEME_ID, isThemeId, type ThemeId } from '../theme';

const STORAGE_KEY = 'domovoy-theme';

const readStoredThemeId = (): ThemeId => {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored && isThemeId(stored) ? stored : DEFAULT_THEME_ID;
  } catch {
    return DEFAULT_THEME_ID;
  }
};

interface ThemeStore {
  themeId: ThemeId;
  setThemeId: (id: ThemeId) => void;
}

export const useThemeStore = create<ThemeStore>((set) => ({
  themeId: readStoredThemeId(),
  setThemeId: (id: ThemeId) => {
    try {
      localStorage.setItem(STORAGE_KEY, id);
    } catch {
      // Private mode / storage disabled — selection just won't persist.
    }
    set({ themeId: id });
  },
}));
