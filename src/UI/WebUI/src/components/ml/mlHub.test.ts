// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import {
  applicableTypesFor, boundOutputOf, consumerBlocksFor, governorTypesFor, measuredSourceCandidates,
  suitableDevicesFor,
} from './mlHub';
import type { BlockCatalogEntry, ControlBlock } from '../../api/blocks';
import type { CapabilityDevice } from '../../api/capabilityDevices';

const thermostatType: BlockCatalogEntry = {
  typeId: 'ml_thermostat',
  title: 'ML thermostat',
  description: '',
  mlTargetCapability: 'temperature',
  inputs: [{ name: 'temperature', kind: 'Number', description: '' }],
  outputs: [
    { id: 'temperature_setpoint', kind: 'Number', writable: true },
    { id: 'proposed_setpoint', kind: 'Number', writable: false },
  ],
  params: [{ name: 'stage', default: 0, description: '' }],
};

const switchType: BlockCatalogEntry = {
  typeId: 'ml_switch',
  title: 'ML switch',
  description: '',
  mlTargetCapability: 'on_off',
  inputs: [{ name: 'on_off', kind: 'Boolean', description: '' }],
  outputs: [{ id: 'on_off', kind: 'Boolean', writable: true }],
  params: [{ name: 'stage', default: 0, description: '' }],
};

const deterministicType: BlockCatalogEntry = {
  typeId: 'thermostat',
  title: 'Thermostat loop',
  description: '',
  inputs: [{ name: 'temperature', kind: 'Number', description: '' }],
  outputs: [{ id: 'on_off', kind: 'Boolean', writable: true }],
  params: [],
};

const catalog = [thermostatType, switchType, deterministicType];

const device = (
  id: string, zoneId: string, caps: { id: string; writable: boolean }[],
): CapabilityDevice => ({
  id,
  name: id,
  adapterSource: 'test',
  zoneId,
  capabilities: caps.map((c) => ({ id: c.id, kind: 'Number', writable: c.writable })),
  state: {},
  isOnline: true,
  lastUpdated: '',
});

// A thermostat-loop virtual device (writable setpoint), a lamp (writable on_off) and two sensors.
const loop = device('loop-1', 'kitchen', [
  { id: 'temperature_setpoint', writable: true }, { id: 'on_off', writable: false },
]);
const lamp = device('lamp-1', 'hall', [{ id: 'on_off', writable: true }]);
const kitchenSensor = device('sensor-kitchen', 'kitchen', [{ id: 'temperature', writable: false }]);
const atticSensor = device('sensor-attic', 'attic', [{ id: 'temperature', writable: false }]);
const devices = [loop, lamp, kitchenSensor, atticSensor];

describe('mlHub applicability joins (Epic 2P, Р9)', () => {
  it('finds governor types by ML target, case-insensitively', () => {
    expect(governorTypesFor(catalog, 'Temperature').map((t) => t.typeId)).toEqual(['ml_thermostat']);
    expect(governorTypesFor(catalog, 'humidity')).toEqual([]);
  });

  it('matches consumer block instances by governor type', () => {
    const blocks = [
      { id: 'b1', typeId: 'ml_thermostat' }, { id: 'b2', typeId: 'thermostat' }, { id: 'b3', typeId: 'ML_THERMOSTAT' },
    ] as ControlBlock[];
    expect(consumerBlocksFor(blocks, [thermostatType]).map((b) => b.id)).toEqual(['b1', 'b3']);
  });

  it('bound output is the first writable catalog output', () => {
    expect(boundOutputOf(thermostatType)).toBe('temperature_setpoint');
  });

  it('suitable devices expose the writable bound output (a loop virtual device qualifies)', () => {
    expect(suitableDevicesFor(thermostatType, devices).map((d) => d.id)).toEqual(['loop-1']);
    // The loop's on_off is read-only, so only the lamp can be governed by the switch.
    expect(suitableDevicesFor(switchType, devices).map((d) => d.id)).toEqual(['lamp-1']);
  });

  it('applicable types for a device come from its writable capabilities', () => {
    expect(applicableTypesFor(catalog, loop).map((t) => t.typeId)).toEqual(['ml_thermostat']);
    expect(applicableTypesFor(catalog, lamp).map((t) => t.typeId)).toEqual(['ml_switch']);
    expect(applicableTypesFor(catalog, kitchenSensor)).toEqual([]);
  });

  it('ranks measured-signal sources: governed device, then same zone, then the rest', () => {
    const ranked = measuredSourceCandidates(thermostatType, devices, loop).map((d) => d.id);
    // The loop itself has no temperature capability, so the kitchen sensor (same zone) leads.
    expect(ranked).toEqual(['sensor-kitchen', 'sensor-attic']);

    const governedSensor = device('gov', 'attic', [{ id: 'temperature', writable: false }]);
    const withSelf = measuredSourceCandidates(thermostatType, [...devices, governedSensor], governedSensor).map((d) => d.id);
    expect(withSelf[0]).toBe('gov'); // same-device signal wins
    expect(withSelf[1]).toBe('sensor-attic'); // then same zone
  });
});
