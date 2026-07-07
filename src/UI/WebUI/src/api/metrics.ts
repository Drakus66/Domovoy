// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

export interface ServiceStatus {
  name: string;
  instance: string;
  isUp: boolean;
}

export interface SystemSummary {
  servicesUp: number | null;
  servicesTotal: number | null;
  mqttConnections: number | null;
  apiRequestRate: number | null;
  memoryMb: number | null;
  collectedAt: string;
}

export const metricsApi = {
  getServicesStatus: (): Promise<ServiceStatus[]> =>
    apiClient.get<ServiceStatus[]>('/api/metrics/services').then((r) => r.data),

  getSummary: (): Promise<SystemSummary> =>
    apiClient.get<SystemSummary>('/api/metrics/summary').then((r) => r.data),
};
