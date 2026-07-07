// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

export interface ZigbeeBridge {
  isOnline: boolean;
  version: string;
  coordinator: { type: string; address: string };
  network: { channel: number; panId: number };
  permitJoin: boolean;
  permitJoinTimeout: number;
  lastUpdated: string;
}

export interface ZigbeeDeviceState {
  state?: string;
  brightness?: number;
  color_temp?: number;
  temperature?: number;
  humidity?: number;
  illuminance?: number;
  contact?: boolean;
  occupancy?: boolean;
  battery?: number;
  linkquality?: number;
  [key: string]: unknown;
}

export interface ZigbeeDevice {
  ieeeAddress: string;
  friendlyName: string;
  type: string;
  supported: boolean;
  model: string;
  vendor: string;
  description: string;
  state: ZigbeeDeviceState;
  lastSeen: string;
}

export interface PermitJoinResponse {
  duration: number;
  message: string;
}

export const zigbeeApi = {
  getBridge: (): Promise<ZigbeeBridge> =>
    apiClient.get<ZigbeeBridge>('/api/zigbee/bridge').then((r) => r.data),

  getDevices: (): Promise<ZigbeeDevice[]> =>
    apiClient.get<ZigbeeDevice[]>('/api/zigbee/devices').then((r) => r.data),

  permitJoin: (duration: number): Promise<PermitJoinResponse> =>
    apiClient.post<PermitJoinResponse>('/api/zigbee/permit-join', { duration }).then((r) => r.data),

  renameDevice: (friendlyName: string, newName: string): Promise<unknown> =>
    apiClient
      .post(`/api/zigbee/devices/${encodeURIComponent(friendlyName)}/rename`, { newName })
      .then((r) => r.data),

  removeDevice: (friendlyName: string): Promise<unknown> =>
    apiClient
      .delete(`/api/zigbee/devices/${encodeURIComponent(friendlyName)}`)
      .then((r) => r.data),
};
