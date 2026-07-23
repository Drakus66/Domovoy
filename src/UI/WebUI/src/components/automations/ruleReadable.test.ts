// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import {
  zoneCoverage, capabilitiesInZone, zonesWithDevices, devicesInZone, readableMatch, zoneNameOf,
} from './ruleReadable';
import { CapabilityDevice, Capability } from '../../api/capabilityDevices';
import { Zone } from '../../api/zones';

const cap = (id: string, writable = false): Capability => ({ id, kind: 'Number', writable });
const dev = (id: string, name: string, zoneId: string, caps: Capability[]): CapabilityDevice => ({
  id, name, adapterSource: 'test', zoneId, state: {}, isOnline: true, lastUpdated: '', capabilities: caps,
});
const zone = (id: string, name: string, order: number): Zone => ({ id, name, order, createdAt: '', updatedAt: '' });

const bedroom = zone('z1', 'Спальня', 0);
const kitchen = zone('z2', 'Кухня', 1);
const garage = zone('z3', 'Гараж', 2); // no devices

const devices: CapabilityDevice[] = [
  dev('d1', 'Датчик спальни', 'z1', [cap('temperature'), cap('humidity')]),
  dev('d2', 'Термостат', 'z1', [cap('temperature'), cap('on_off', true)]),
  dev('d3', 'Плита', 'z2', [cap('on_off', true)]),
];
const zones = [bedroom, kitchen, garage];

describe('ruleReadable — zone helpers (Epic 3G)', () => {
  it('devicesInZone lists only devices assigned to the zone', () => {
    expect(devicesInZone(devices, 'z1').map((d) => d.id)).toEqual(['d1', 'd2']);
    expect(devicesInZone(devices, 'z3')).toEqual([]);
    expect(devicesInZone(devices, null)).toEqual([]);
  });

  it('zoneCoverage counts devices reachable by a zone-scoped capability match', () => {
    expect(zoneCoverage(devices, 'z1', 'temperature')).toBe(2); // both bedroom devices
    expect(zoneCoverage(devices, 'z1', 'on_off')).toBe(1); // only the thermostat
    expect(zoneCoverage(devices, 'z2', 'temperature')).toBe(0); // kitchen has none
    expect(zoneCoverage(devices, 'z1', null)).toBe(2); // no capability ⇒ whole zone
  });

  it('capabilitiesInZone unions the zone capabilities, deduped, writable-filterable', () => {
    expect(capabilitiesInZone(devices, 'z1').map((c) => c.id).sort())
      .toEqual(['humidity', 'on_off', 'temperature']);
    expect(capabilitiesInZone(devices, 'z1', { writableOnly: true }).map((c) => c.id))
      .toEqual(['on_off']);
  });

  it('zonesWithDevices keeps only populated zones with their counts', () => {
    expect(zonesWithDevices(zones, devices)).toEqual([
      { zone: bedroom, count: 2 },
      { zone: kitchen, count: 1 },
    ]);
  });

  it('zoneNameOf resolves names and the unassigned fallback', () => {
    expect(zoneNameOf(zones, 'z1')).toBe('Спальня');
    expect(zoneNameOf(zones, '')).toBe('Без зоны');
    expect(zoneNameOf(zones, '00000000-0000-0000-0000-000000000000')).toBe('Без зоны');
  });
});

describe('ruleReadable — readableMatch (Epic 3G)', () => {
  it('renders a single-device match as a sentence', () => {
    expect(readableMatch(devices, zones, { deviceId: 'd1', capabilityId: 'temperature', operator: 'lt', value: 18 }))
      .toBe('температура у «Датчик спальни» ниже 18');
  });

  it('renders a zone-scoped match as "in «Zone» …"', () => {
    expect(readableMatch(devices, zones, { zoneId: 'z1', capabilityId: 'temperature', operator: 'gt', value: 25 }))
      .toBe('в «Спальня» температура выше 25');
  });

  it('renders the "changed" operator without a value, and booleans as on/off', () => {
    expect(readableMatch(devices, zones, { deviceId: 'd2', capabilityId: 'on_off', operator: 'changed' }))
      .toBe('питание у «Термостат» изменилось');
    expect(readableMatch(devices, zones, { deviceId: 'd2', capabilityId: 'on_off', operator: 'eq', value: true }))
      .toBe('питание у «Термостат» равно вкл');
  });

  it('returns an empty string when no target is selected yet', () => {
    expect(readableMatch(devices, zones, { capabilityId: 'temperature', operator: 'lt', value: 5 })).toBe('');
    expect(readableMatch(devices, zones, {})).toBe('');
  });
});
