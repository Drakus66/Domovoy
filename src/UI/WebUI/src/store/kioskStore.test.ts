// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, beforeEach } from 'vitest';
import { useKioskStore, DEFAULT_KIOSK_CONFIG, type KioskConfig } from './kioskStore';

const KEY = 'domovoy.kiosk';

describe('kioskStore', () => {
  beforeEach(() => {
    localStorage.clear();
    // Reset to a known un-kiosked state (the store is a module singleton).
    useKioskStore.setState({ enabled: false, config: { ...DEFAULT_KIOSK_CONFIG } });
  });

  it('enter turns kiosk on, stores the config, and persists it', () => {
    const config: KioskConfig = { tabPath: '/t/kitchen', idleReturnSeconds: 30, dimSeconds: 90 };
    useKioskStore.getState().enter(config);

    const state = useKioskStore.getState();
    expect(state.enabled).toBe(true);
    expect(state.config).toEqual(config);

    const persisted = JSON.parse(localStorage.getItem(KEY) as string);
    expect(persisted.enabled).toBe(true);
    expect(persisted.config).toEqual(config);
  });

  it('exit turns kiosk off and persists the off state', () => {
    useKioskStore.getState().enter({ tabPath: '/', idleReturnSeconds: 10, dimSeconds: 20 });
    useKioskStore.getState().exit();

    expect(useKioskStore.getState().enabled).toBe(false);
    const persisted = JSON.parse(localStorage.getItem(KEY) as string);
    expect(persisted.enabled).toBe(false);
    // The config is retained so re-entering keeps the last screen/timeouts.
    expect(persisted.config.tabPath).toBe('/');
  });
});
