// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

export type VariableType = 'Number' | 'Boolean' | 'String' | 'DateTime' | 'List';

/**
 * A named, persistent global variable (roadmap Epic 3E, "Hub Variables"; matches Domovoy.Contracts
 * GlobalVariable). Also projected server-side as its own virtual capability device with one capability
 * `value` — so it shows up for free in `capabilityDevicesApi.getDevices()` and needs no special-casing in
 * device/capability pickers.
 */
export interface GlobalVariable {
  id: string;
  name: string;
  type: VariableType;
  description?: string | null;
  /** double for Number, bool for Boolean, string for String/DateTime(ISO-8601)/List(JSON-encoded). */
  value: unknown;
  createdAt: string;
  updatedAt: string;
}

export type NewVariable = Omit<GlobalVariable, 'id' | 'createdAt' | 'updatedAt'>;

export const variablesApi = {
  getVariables: (): Promise<GlobalVariable[]> =>
    apiClient.get<GlobalVariable[]>('/api/variables').then((r) => r.data),

  getVariable: (id: string): Promise<GlobalVariable> =>
    apiClient.get<GlobalVariable>(`/api/variables/${encodeURIComponent(id)}`).then((r) => r.data),

  createVariable: (variable: NewVariable): Promise<GlobalVariable> =>
    apiClient.post<GlobalVariable>('/api/variables', variable).then((r) => r.data),

  updateVariable: (id: string, variable: GlobalVariable): Promise<void> =>
    apiClient.put(`/api/variables/${encodeURIComponent(id)}`, variable).then(() => undefined),

  /** Update just the live value (e.g. from a quick-edit control), without touching name/type/description. */
  setValue: (id: string, value: unknown): Promise<void> =>
    apiClient.put(`/api/variables/${encodeURIComponent(id)}/value`, { value }).then(() => undefined),

  deleteVariable: (id: string): Promise<void> =>
    apiClient.delete(`/api/variables/${encodeURIComponent(id)}`).then(() => undefined),
};
