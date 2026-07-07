import { describe, expect, it } from 'vitest';
import type { Capability, CapabilityDevice } from '../../api/capabilityDevices';
import { deriveSpheres, sphereCategoryFromTabId, sphereTabId } from './spheres';

const cap = (id: string, writable = false, kind = 'Number'): Capability => ({
  id, kind, writable,
});

const device = (overrides: Partial<CapabilityDevice>): CapabilityDevice => ({
  id: Math.random().toString(36).slice(2),
  name: 'Device',
  adapterSource: 'test',
  zoneId: '',
  capabilities: [],
  state: {},
  isOnline: true,
  lastUpdated: new Date().toISOString(),
  ...overrides,
});

describe('deriveSpheres', () => {
  it('buckets devices by category via archetype', () => {
    const spheres = deriveSpheres([
      device({ autoArchetype: 'light' }),
      device({ autoArchetype: 'light' }),
      device({ autoArchetype: 'thermostat' }),
    ], []);

    expect(spheres).toEqual([
      { id: 'sphere:light', category: 'light', count: 2 },
      { id: 'sphere:climate', category: 'climate', count: 1 },
    ]);
  });

  it('falls back to the capability heuristic when the archetype is unknown', () => {
    const spheres = deriveSpheres([
      device({ capabilities: [cap('brightness', true)] }),          // light
      device({ capabilities: [cap('on_off', true, 'Boolean')] }),   // switch
      device({ capabilities: [cap('temperature')] }),               // sensor
    ], []);

    expect(spheres.map((s) => s.category)).toEqual(['light', 'switch', 'sensor']);
  });

  it('honours a manual archetype override above the auto one', () => {
    const spheres = deriveSpheres(
      [device({ autoArchetype: 'sensor', archetype: 'lock' })], []);

    expect(spheres).toEqual([{ id: 'sphere:security', category: 'security', count: 1 }]);
  });

  it('omits categories with no devices', () => {
    const spheres = deriveSpheres([device({ autoArchetype: 'light' })], []);

    expect(spheres.map((s) => s.category)).toEqual(['light']);
  });

  it('filters hidden spheres but keeps the rest', () => {
    const spheres = deriveSpheres([
      device({ autoArchetype: 'light' }),
      device({ autoArchetype: 'switch' }),
    ], ['switch']);

    expect(spheres.map((s) => s.category)).toEqual(['light']);
  });

  it('keeps the fixed display order regardless of device order', () => {
    const spheres = deriveSpheres([
      device({ autoArchetype: 'energy_meter' }),
      device({ autoArchetype: 'lock' }),
      device({ autoArchetype: 'light' }),
    ], []);

    expect(spheres.map((s) => s.category)).toEqual(['light', 'security', 'energy']);
  });

  it('returns no spheres for an empty home', () => {
    expect(deriveSpheres([], [])).toEqual([]);
  });
});

describe('sphere tab ids', () => {
  it('round-trips a category through the tab id', () => {
    expect(sphereCategoryFromTabId(sphereTabId('climate'))).toBe('climate');
  });

  it('rejects non-sphere and unknown ids', () => {
    expect(sphereCategoryFromTabId('all')).toBeNull();
    expect(sphereCategoryFromTabId('sphere:bogus')).toBeNull();
  });
});
