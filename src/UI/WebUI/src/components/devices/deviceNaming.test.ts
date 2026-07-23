// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import {
  isCrypticName, autoDeviceName, deviceLabel,
  withZonePointer, stripZonePointer, proposeZoneName, hasZonePointer,
} from './deviceNaming';

// The test i18n is pinned to Russian (see test/setup.ts), so assertions use the RU labels.

const dev = (over: Partial<CapabilityDevice>): CapabilityDevice => ({
  id: 'd1', name: '0xa4c1383e04dbda65', adapterSource: 'Zigbee2Mqtt', zoneId: '',
  capabilities: [], state: {}, isOnline: true, lastUpdated: '', ...over,
});

describe('isCrypticName', () => {
  it('flags Zigbee IEEE ids, GUIDs and blanks', () => {
    expect(isCrypticName('0xa4c1383e04dbda65')).toBe(true);
    expect(isCrypticName('123e4567-e89b-12d3-a456-426614174000')).toBe(true);
    expect(isCrypticName('')).toBe(true);
    expect(isCrypticName('   ')).toBe(true);
  });
  it('passes through human names', () => {
    expect(isCrypticName('Гостиная — климат')).toBe(false);
    expect(isCrypticName('Розетка')).toBe(false);
  });
});

describe('autoDeviceName', () => {
  it('uses a localized type label from the archetype', () => {
    expect(autoDeviceName(dev({ autoArchetype: 'motion' }))).toBe('Датчик движения');
    expect(autoDeviceName(dev({ archetype: 'light', autoArchetype: 'sensor' }))).toBe('Свет');
  });
  it('falls back to the raw name, then a generic label, for an unknown type', () => {
    expect(autoDeviceName(dev({ name: 'Кухонный свет', autoArchetype: 'unknown' }))).toBe('Кухонный свет');
    expect(autoDeviceName(dev({ name: '', autoArchetype: 'unknown' }))).toBe('Устройство');
  });
});

describe('deviceLabel', () => {
  it('prefers a user alias', () => {
    expect(deviceLabel(dev({ alias: 'Люстра', autoArchetype: 'light' }))).toBe('Люстра');
  });
  it('replaces a cryptic name with a type label', () => {
    expect(deviceLabel(dev({ autoArchetype: 'light' }))).toBe('Свет');
  });
  it('keeps a friendly adapter name', () => {
    expect(deviceLabel(dev({ name: 'Гостиная — климат', autoArchetype: 'climate_sensor' }))).toBe('Гостиная — климат');
  });
});

describe('zone pointer', () => {
  it('appends / strips / detects the pointer', () => {
    expect(withZonePointer('Люстра', 'Гостиная')).toBe('Люстра в Гостиная');
    expect(stripZonePointer('Люстра в Гостиная', ['Гостиная'])).toBe('Люстра');
    expect(stripZonePointer('Люстра', ['Гостиная'])).toBe('Люстра');
    expect(hasZonePointer('Люстра в Гостиная', 'Гостиная')).toBe(true);
    expect(hasZonePointer('Люстра', 'Гостиная')).toBe(false);
  });

  it('proposeZoneName re-points an existing pointer and clears on unassign', () => {
    expect(proposeZoneName('Люстра', 'Гостиная', [])).toBe('Люстра в Гостиная');
    expect(proposeZoneName('Люстра в Кухня', 'Гостиная', ['Кухня'])).toBe('Люстра в Гостиная');
    expect(proposeZoneName('Люстра в Гостиная', null, ['Гостиная'])).toBe('Люстра');
  });
});
