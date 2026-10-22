// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import type { EnergyBreakdownResult } from '../../../api/energy';
import { buildPowerSunburst, type PowerDeviceRow, type PowerSunburstPalette } from './powerSunburst';

const palette: PowerSunburstPalette = {
  l1: '#111111', l2: '#222222', l3: '#333333', rest: '#444444', neutral: '#999999',
};
const labels = { root: 'Дом', unaccounted: 'Неучтённое', unmapped: 'Не отнесено' };

const breakdown: EnergyBreakdownResult = {
  from: '', to: '',
  nodes: [
    {
      nodeId: 'supply', name: 'Ввод', kind: 'supply', parentId: null, phase: null,
      kwh: 10, powerW: 1000, deviceCount: 3,
      meterKwh: 12, meterPowerW: 1200, unaccountedKwh: 2, limitWatts: null,
    },
    {
      nodeId: 'c1', name: 'Свет', kind: 'circuit', parentId: 'supply', phase: 'l1',
      kwh: 6, powerW: 600, deviceCount: 2, limitWatts: 3680,
    },
    {
      nodeId: 'c2', name: 'Розетки', kind: 'circuit', parentId: 'supply', phase: 'l2',
      kwh: 4, powerW: 400, deviceCount: 1,
    },
  ],
  phases: [],
  unmappedKwh: 1.5,
  unmappedPowerW: 150,
};

const devices: PowerDeviceRow[] = [
  { id: 'd1', name: 'Лампа', circuitId: 'c1', kwh: 4, watts: 300 },
  { id: 'd2', name: 'Чайник', circuitId: 'c2', kwh: 4, watts: 400 },
];

describe('buildPowerSunburst', () => {
  it('returns null for an empty topology', () => {
    expect(buildPowerSunburst({ ...breakdown, nodes: [] }, [], 'kwh', palette, labels)).toBeNull();
  });

  it('avoids double counting: subtree sums never overflow the parent span', () => {
    const root = buildPowerSunburst(breakdown, devices, 'kwh', palette, labels)!;
    const supply = root.children!.find((c) => c.id === 'supply')!;
    // The supply's span = its known subtree (10) + its meter's unaccounted slice (2)…
    expect(supply.value).toBe(12);
    // …and its children cover exactly that: c1 (6) + c2 (4) + the gray unaccounted slice (2).
    expect(supply.children!.reduce((s, k) => s + k.value, 0)).toBe(12);
  });

  it('adds gray synthetic segments for unaccounted and unmapped consumption', () => {
    const root = buildPowerSunburst(breakdown, devices, 'kwh', palette, labels)!;
    const supply = root.children!.find((c) => c.id === 'supply')!;
    const unaccounted = supply.children!.find((c) => c.id === 'unaccounted:supply')!;
    expect(unaccounted.value).toBe(2);
    expect(unaccounted.fill).toBe(palette.neutral);

    const unmapped = root.children!.find((c) => c.id === '__unmapped')!;
    expect(unmapped.value).toBe(1.5);
    expect(unmapped.fill).toBe(palette.neutral);
    expect(root.value).toBe(12 + 1.5);
  });

  it('colors branches by inherited phase and shades deeper rings', () => {
    const root = buildPowerSunburst(breakdown, devices, 'kwh', palette, labels)!;
    const supply = root.children!.find((c) => c.id === 'supply')!;
    // No phase on the supply → the "rest" hue at depth 0 (no alpha step).
    expect(supply.fill).toBe('#444444');
    const c1 = supply.children!.find((c) => c.id === 'c1')!;
    const c2 = supply.children!.find((c) => c.id === 'c2')!;
    expect(c1.fill).toBe('#111111CC'); // l1 hue, depth-1 alpha
    expect(c2.fill).toBe('#222222CC'); // l2 hue
  });

  it('joins devices onto their circuit as the outer ring', () => {
    const root = buildPowerSunburst(breakdown, devices, 'kwh', palette, labels)!;
    const supply = root.children!.find((c) => c.id === 'supply')!;
    const c1 = supply.children!.find((c) => c.id === 'c1')!;
    const lamp = c1.children!.find((c) => c.id === 'dev:d1')!;
    expect(lamp.name).toBe('Лампа');
    expect(lamp.value).toBe(4);
    expect(lamp.fill).toBe('#11111199'); // one alpha step deeper than its circuit
  });

  it('switches to live watts, deriving unaccounted from the meter draw', () => {
    const root = buildPowerSunburst(breakdown, devices, 'watts', palette, labels)!;
    const supply = root.children!.find((c) => c.id === 'supply')!;
    // 1000 W known + (1200 meter − 1000) unaccounted.
    expect(supply.value).toBe(1200);
    const unaccounted = supply.children!.find((c) => c.id === 'unaccounted:supply')!;
    expect(unaccounted.value).toBe(200);
    const unmapped = root.children!.find((c) => c.id === '__unmapped')!;
    expect(unmapped.value).toBe(150);
  });

  it('sorts children by value, heaviest first', () => {
    const root = buildPowerSunburst(breakdown, devices, 'kwh', palette, labels)!;
    const supply = root.children!.find((c) => c.id === 'supply')!;
    expect(supply.children!.map((c) => c.id)).toEqual(['c1', 'c2', 'unaccounted:supply']);
  });
});
