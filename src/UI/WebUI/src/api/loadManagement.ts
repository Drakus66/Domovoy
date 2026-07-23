// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** Well-known power-source signals (roadmap Epic 3C-LM). Open set — a deployment may report a custom one. */
export type PowerSource = 'grid' | 'grid_peak' | 'battery' | 'solar' | 'off';

export const POWER_SOURCES: PowerSource[] = ['grid', 'grid_peak', 'battery', 'solar', 'off'];

/** A power budget (W) that applies while the house's power_source signal equals this value. */
export interface PowerBudget {
  powerSource: string;
  limitWatts: number;
}

/** Load-shedding configuration (matches DbGateway LoadManagementSettings). Off by default — an empty/disabled
 *  document is a no-op. */
export interface LoadManagementSettings {
  id?: string;
  enabled: boolean;
  budgets: PowerBudget[];
  restoreMarginWatts: number;
  minDwellSeconds: number;
  /** Per-phase limits (W) for a polyphase intake (Epic 3C-D); empty ⇒ fall back to breaker ratings. */
  phaseLimits?: PhaseLimit[];
  /** Per-circuit overrides (W) of the breaker rating (Epic 3C-D). */
  circuitLimits?: CircuitLimit[];
  updatedAt?: string;
}

/** A limit on one phase of the intake (matches DbGateway PhaseLimit). */
export interface PhaseLimit {
  phase: string;
  limitWatts: number;
}

/** A limit on one circuit, overriding its breaker rating (matches DbGateway CircuitLimit). */
export interface CircuitLimit {
  circuitId: string;
  limitWatts: number;
}

/** Single phases of a three-phase intake, in display order. */
export const SINGLE_PHASES = ['l1', 'l2', 'l3'] as const;

export type LoadSheddingTier = 'critical' | 'sheddable' | 'unmanaged';

/** Per-device load-shedding profile (matches DbGateway LoadSheddingProfile). */
export interface LoadSheddingProfile {
  enabled: boolean;
  protected: boolean;
  controlCapabilityId: string;
  curtailable: boolean;
  curtailedValue: number | null;
  restoreValue: number | null;
  modeTier: Record<string, string>;
  modePriority: Record<string, number>;
}

export const loadManagementApi = {
  getSettings: (): Promise<LoadManagementSettings> =>
    apiClient.get<LoadManagementSettings>('/api/settings/load-management').then((r) => r.data),

  saveSettings: (body: {
    enabled: boolean; budgets: PowerBudget[]; restoreMarginWatts: number; minDwellSeconds: number;
    phaseLimits?: PhaseLimit[]; circuitLimits?: CircuitLimit[];
  }): Promise<LoadManagementSettings> =>
    apiClient.put<LoadManagementSettings>('/api/settings/load-management', body).then((r) => r.data),

  /** Set/clear a device's load-shedding profile (null clears it — the device becomes unmanaged again). */
  setDeviceProfile: (deviceId: string, loadShedding: LoadSheddingProfile | null): Promise<void> =>
    apiClient
      .put(`/api/capability-devices/${encodeURIComponent(deviceId)}/load-shedding`, { loadShedding })
      .then(() => undefined),
};
