// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { create } from 'zustand';

/**
 * Kiosk mode (mobile-app / remote-access track, Epic 2O.2). Turns the WebUI into a wall-panel: pinned to one
 * dashboard, no navigation chrome, auto-return to the pinned screen and screen-dim on idle, and a deliberately
 * hidden exit. The config is persisted locally (this device is the panel), so a reload comes back up in kiosk
 * mode. Central multi-panel registration (a `panels` collection) is a later increment; a single panel needs only
 * this local config.
 */

const KEY = 'domovoy.kiosk';

export interface KioskConfig {
  /** Path of the pinned screen: '/' (home) or '/t/<dashboardId>' for a custom dashboard. */
  tabPath: string;
  /** Seconds of inactivity before returning to the pinned screen. 0 = never. */
  idleReturnSeconds: number;
  /** Seconds of inactivity before dimming the screen. 0 = never. */
  dimSeconds: number;
}

interface KioskStore {
  enabled: boolean;
  config: KioskConfig;
  enter: (config: KioskConfig) => void;
  exit: () => void;
}

export const DEFAULT_KIOSK_CONFIG: KioskConfig = {
  tabPath: '/',
  idleReturnSeconds: 60,
  dimSeconds: 120,
};

function loadState(): { enabled: boolean; config: KioskConfig } {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as { enabled?: boolean; config?: Partial<KioskConfig> };
      return {
        enabled: Boolean(parsed.enabled),
        config: { ...DEFAULT_KIOSK_CONFIG, ...(parsed.config ?? {}) },
      };
    }
  } catch {
    // corrupt / unavailable storage → start un-kiosked
  }
  return { enabled: false, config: { ...DEFAULT_KIOSK_CONFIG } };
}

function persist(enabled: boolean, config: KioskConfig): void {
  try {
    localStorage.setItem(KEY, JSON.stringify({ enabled, config }));
  } catch {
    // storage unavailable (private mode) — kiosk lives only for this session
  }
}

export const useKioskStore = create<KioskStore>((set, get) => {
  const initial = loadState();
  return {
    enabled: initial.enabled,
    config: initial.config,

    enter: (config: KioskConfig) => {
      persist(true, config);
      set({ enabled: true, config });
    },

    exit: () => {
      persist(false, get().config);
      set({ enabled: false });
    },
  };
});
