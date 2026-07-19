// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** One manageable unit on the System page (a .NET service, or an infra container when Docker control is on). */
export interface SystemServiceInfo {
  name: string;
  /** gateway | service | container */
  kind: string;
  /** True → restartable over the bus (a .NET service). False → container-only (needs Docker control). */
  selfRestart: boolean;
  /** Live container state/status — only present when Docker control is enabled. */
  state?: string | null;
  status?: string | null;
}

export interface SystemServicesResponse {
  dockerEnabled: boolean;
  services: SystemServiceInfo[];
}

export const systemApi = {
  getServices: (): Promise<SystemServicesResponse> =>
    apiClient.get<SystemServicesResponse>('/api/system/services').then((r) => r.data),

  /** Restart every .NET service (self-restart broadcast). */
  restartAll: (): Promise<void> =>
    apiClient.post('/api/system/restart').then(() => undefined),

  /** Restart one target: self-restart by default, or a Docker container restart when `container` is set. */
  restartService: (name: string, container = false): Promise<void> =>
    apiClient
      .post(`/api/system/services/${encodeURIComponent(name)}/restart${container ? '?container=true' : ''}`)
      .then(() => undefined),

  /** Container lifecycle action (Docker path only). */
  containerAction: (name: string, action: 'restart' | 'stop' | 'start'): Promise<void> =>
    apiClient.post(`/api/system/containers/${encodeURIComponent(name)}/${action}`).then(() => undefined),
};
