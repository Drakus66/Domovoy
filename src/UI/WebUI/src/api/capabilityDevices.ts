// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

export type CapabilityKind = 'Boolean' | 'Number' | 'Enum' | 'Color' | 'Text' | 'Action';

/** A single capability of a device (matches DbGateway CapabilityDocument). */
export interface Capability {
  id: string;
  kind: CapabilityKind | string;
  writable: boolean;
  unit?: string | null;
  min?: number | null;
  max?: number | null;
  /** Numeric step for a Number control. */
  step?: number | null;
  /** Allowed values for an Enum capability (drives a dropdown). */
  values?: string[] | null;
  /** UI editor hint for a writable value: 'geo' (map picker) | 'time' (time input). */
  editor?: string | null;
}

/** Capability device read-model (matches DbGateway CapabilityDeviceDocument). */
export interface CapabilityDevice {
  id: string;
  name: string;
  adapterSource: string;
  model?: string | null;
  zoneId: string;
  capabilities: Capability[];
  state: Record<string, unknown>;
  isOnline: boolean;
  /** Auto-inferred semantic archetype (roadmap Epic 2D). */
  autoArchetype?: string;
  /** Manual override; null/absent ⇒ use autoArchetype. */
  archetype?: string | null;
  lastUpdated: string;
}

/** Well-known device archetypes (mirrors Domovoy.Contracts DeviceArchetypes, Epic 2D). */
export const DEVICE_ARCHETYPES = [
  'light', 'switch', 'thermostat', 'climate_sensor', 'motion', 'contact',
  'lock', 'valve', 'energy_meter', 'sensor', 'control_block', 'sun', 'clock', 'calendar', 'unknown',
] as const;

/** Effective archetype = manual override if set, else the auto-inferred value. */
export const effectiveArchetype = (d: CapabilityDevice): string => d.archetype || d.autoArchetype || 'unknown';

export const capabilityDevicesApi = {
  /** List capability devices (Zigbee, native and emulator devices, normalized). */
  getDevices: (): Promise<CapabilityDevice[]> =>
    apiClient.get<CapabilityDevice[]>('/api/capability-devices').then((r) => r.data),

  /**
   * Send a capability-addressed command, e.g. { on_off: true, brightness: 50 }.
   * Published as DeviceCommandV1; the owning adapter encodes & applies it.
   */
  sendCommand: (deviceId: string, set: Record<string, unknown>): Promise<void> =>
    apiClient
      .post(`/api/device-control/${encodeURIComponent(deviceId)}/set`, set)
      .then(() => undefined),

  /** Bind a device to a zone (roadmap P0-3); pass null/'' to unassign. */
  assignZone: (deviceId: string, zoneId: string | null): Promise<void> =>
    apiClient
      .put(`/api/capability-devices/${encodeURIComponent(deviceId)}/zone`, { zoneId })
      .then(() => undefined),

  /** Override the semantic archetype (roadmap Epic 2D); pass null to revert to auto-classification. */
  setArchetype: (deviceId: string, archetype: string | null): Promise<void> =>
    apiClient
      .put(`/api/capability-devices/${encodeURIComponent(deviceId)}/archetype`, { archetype })
      .then(() => undefined),

  /**
   * Remove an offline device from the registry (409 while online). Reversible by design: if the
   * device powers back up it re-announces and goes through discovery again as a fresh arrival.
   */
  deleteDevice: (deviceId: string): Promise<void> =>
    apiClient
      .delete(`/api/capability-devices/${encodeURIComponent(deviceId)}`)
      .then(() => undefined),
};

/**
 * Infrastructure devices: platform virtual sensors (sun/time/calendar/home) and control-block
 * projections. They stay first-class everywhere a device can be referenced (rule editors,
 * dashboard pickers, the registry's "service" toggle) but are kept off the home-screen tabs.
 */
export const SERVICE_ADAPTER_SOURCES = ['System', 'ControlBlock'] as const;

export const isServiceDevice = (d: CapabilityDevice): boolean =>
  (SERVICE_ADAPTER_SOURCES as readonly string[]).includes(d.adapterSource);

/** Guid.Empty / blank zone ids both mean "unassigned". */
export const isUnassignedZone = (zoneId?: string | null): boolean =>
  !zoneId || zoneId.trim() === '' || zoneId === '00000000-0000-0000-0000-000000000000';
