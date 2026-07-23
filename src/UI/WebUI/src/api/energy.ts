// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';
import type { EnergyProfile } from './capabilityDevices';

/** Energy role pinned on a device to keep kWh totals honest (Epic 3C). null ⇒ a normal consumer that counts
 *  toward totals; `mains` ⇒ whole-home/aggregate meter (grand total only). Leaving a device out of the totals
 *  entirely is the accounting toggle (`energyProfile.track = false`), not a role. */
export type EnergyRole = 'mains' | 'consumer';

/** Per-device consumption over a window (matches DbGateway EnergyDeviceConsumption). */
export interface EnergyDeviceConsumption {
  deviceId: string;
  name: string;
  zoneId: string;
  archetype?: string | null;
  energyRole?: EnergyRole | null;
  kwh: number;
}

/** Consumption for every energy-metered device plus honest totals (matches DbGateway EnergyConsumptionResult). */
export interface EnergyConsumptionResult {
  from: string;
  to: string;
  bucket: string;
  devices: EnergyDeviceConsumption[];
  consumerTotalKwh: number;
  mainsTotalKwh: number;
}

export interface EnergyConsumptionQuery {
  from?: string;
  to?: string;
  bucket?: 'minute' | 'hour' | 'day';
}

/** Consumption + cost for one tariff zone (matches DbGateway EnergyCostZone). */
export interface EnergyCostZone {
  zone: string;
  kwh: number;
  cost: number;
}

/** Household energy cost over a window, priced by the tariff (matches DbGateway EnergyCostResult). */
export interface EnergyCostResult {
  from: string;
  to: string;
  currency: string;
  totalKwh: number;
  totalCost: number;
  zones: EnergyCostZone[];
}

/** One node of the electrical tree with its subtree's consumption (matches DbGateway EnergyNodeBreakdown). */
export interface EnergyNodeBreakdown {
  nodeId: string;
  name: string;
  kind: string;
  parentId?: string | null;
  phase?: string | null;
  kwh: number;
  powerW: number;
  deviceCount: number;
  /** What the node's own meter measured over the window (null ⇒ the node has no meter). */
  meterKwh?: number | null;
  meterPowerW?: number | null;
  /** Meter reading minus what the known devices explain — the load nothing accounts for. */
  unaccountedKwh?: number | null;
  /** Breaker rating in watts (A × V), when the node states one. */
  limitWatts?: number | null;
}

/** Consumption and live draw on one phase (matches DbGateway EnergyPhaseBreakdown). */
export interface EnergyPhaseBreakdown {
  phase: string;
  kwh: number;
  powerW: number;
  limitWatts?: number | null;
}

/** Consumption projected onto the electrical topology (matches DbGateway EnergyBreakdownResult). */
export interface EnergyBreakdownResult {
  from: string;
  to: string;
  nodes: EnergyNodeBreakdown[];
  phases: EnergyPhaseBreakdown[];
  /** Tracked devices not attached to any circuit — counted in totals, but not attributable to a line. */
  unmappedKwh: number;
  unmappedPowerW: number;
}

export const energyApi = {
  /** Per-device kWh + totals for a window (Top Consumers / zone breakdown are derived client-side). */
  getConsumption: (q: EnergyConsumptionQuery = {}): Promise<EnergyConsumptionResult> =>
    apiClient.get<EnergyConsumptionResult>('/api/energy/consumption', { params: q }).then((r) => r.data),

  /** Household kWh + money for a window, broken down by tariff zone. */
  getCost: (q: { from?: string; to?: string } = {}): Promise<EnergyCostResult> =>
    apiClient.get<EnergyCostResult>('/api/energy/cost', { params: q }).then((r) => r.data),

  /** Consumption/draw per circuit and per phase, with the balance check (Epic 3C-D). */
  getBreakdown: (q: { from?: string; to?: string } = {}): Promise<EnergyBreakdownResult> =>
    apiClient.get<EnergyBreakdownResult>('/api/energy/breakdown', { params: q }).then((r) => r.data),

  /** Set/clear a device's energy profile (null resets it to the defaults). */
  setEnergyProfile: (deviceId: string, energyProfile: EnergyProfile | null): Promise<void> =>
    apiClient
      .put(`/api/capability-devices/${encodeURIComponent(deviceId)}/energy-profile`, { energyProfile })
      .then(() => undefined),
};
