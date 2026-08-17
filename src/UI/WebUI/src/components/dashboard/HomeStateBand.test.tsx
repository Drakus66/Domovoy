// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import {
  summarizeHome, pickClimateTrendDevice, pickEnergyTrendDevice, openSecurityItems, topPowerConsumers,
} from './homeSummary';
import type { CapabilityDevice } from '../../api/capabilityDevices';

const dev = (state: Record<string, unknown>): CapabilityDevice => ({
  id: Math.random().toString(36).slice(2),
  name: 'd',
  adapterSource: 'test',
  zoneId: '',
  capabilities: Object.keys(state).map((id) => ({ id, kind: 'Number', writable: false })),
  state,
  isOnline: true,
  lastUpdated: '',
});

describe('summarizeHome (band aggregates)', () => {
  it('averages climate readings across sensors', () => {
    const s = summarizeHome([
      dev({ temperature: 20, humidity: 40 }),
      dev({ temperature: 24, humidity: 50, co2: 600 }),
    ]);
    expect(s.climate.temp).toBe(22);
    expect(s.climate.humidity).toBe(45);
    expect(s.climate.co2).toBe(600);
  });

  it('counts unlocked locks and open contacts as "open"', () => {
    const s = summarizeHome([
      dev({ lock: true }),          // locked
      dev({ lock: false }),         // unlocked → open
      dev({ contact: true }),       // open door
      dev({ contact: false }),      // closed
    ]);
    expect(s.security.locks).toBe(2);
    expect(s.security.openCount).toBe(2);
  });

  it('sums instantaneous power and cumulative energy', () => {
    const s = summarizeHome([
      dev({ power: 95 }),
      dev({ power: 245.4, energy: 1.2 }),
      dev({ energy: 3.1 }),
    ]);
    expect(s.energy.watts).toBe(340);
    expect(s.energy.kwh).toBe(4.3);
  });

  it('leaves climate undefined when no sensors report it', () => {
    const s = summarizeHome([dev({ on_off: true })]);
    expect(s.climate.temp).toBeUndefined();
    expect(s.security.locks).toBe(0);
    expect(s.energy.watts).toBe(0);
  });
});

describe('band flip-side helpers', () => {
  it('picks the first device with a finite temperature for the climate trend', () => {
    const noTemp = dev({ humidity: 40 });
    const withTemp = dev({ temperature: 21 });
    expect(pickClimateTrendDevice([noTemp, withTemp, dev({ temperature: 25 })])).toBe(withTemp);
    expect(pickClimateTrendDevice([noTemp])).toBeUndefined();
  });

  it('picks the heaviest current consumer for the energy trend', () => {
    const heavy = dev({ power: 1500 });
    expect(pickEnergyTrendDevice([dev({ power: 95 }), heavy, dev({ on_off: true })])).toBe(heavy);
    expect(pickEnergyTrendDevice([dev({ on_off: true })])).toBeUndefined();
  });

  it('lists unlocked locks and open contacts with their kind', () => {
    const unlocked = dev({ lock: false });
    const open = dev({ contact: true });
    const items = openSecurityItems([dev({ lock: true }), unlocked, open, dev({ contact: false })]);
    expect(items).toEqual([
      { device: unlocked, kind: 'lock' },
      { device: open, kind: 'contact' },
    ]);
  });

  it('ranks consumers by watts, drops idle devices and caps the list', () => {
    const devices = [
      dev({ power: 10 }), dev({ power: 300 }), dev({ power: 0 }),
      dev({ power: 150 }), dev({ power: 40 }), dev({ power: 25 }),
    ];
    const top = topPowerConsumers(devices, 4);
    expect(top.map((c) => c.watts)).toEqual([300, 150, 40, 25]);
  });
});
