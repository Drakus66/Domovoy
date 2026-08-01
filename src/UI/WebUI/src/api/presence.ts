// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** A tracked household member (roadmap Epic 3D). Mirrors DbGateway Resident. */
export interface Resident {
  id: string;
  displayName: string;
  userId: string | null;
  ownTracksId: string | null;
  trackingEnabled: boolean;
  createdAt?: string;
  updatedAt?: string;
}

/** Create/update body (mirrors DbGateway ResidentInput). */
export interface ResidentInput {
  displayName: string;
  userId: string | null;
  ownTracksId: string | null;
  trackingEnabled: boolean;
}

/** Presence-layer settings (mirrors DbGateway PresenceSettings). */
export interface PresenceSettings {
  id?: string;
  homeRadiusMeters: number;
  awayGraceSeconds: number;
  ownTracksToken: string | null;
  updatedAt?: string;
}

/** One resident's live status (mirrors AutomationService PresenceState.ResidentStatus). */
export interface ResidentStatus {
  id: string;
  displayName: string;
  ownTracksId: string | null;
  trackingEnabled: boolean;
  home: boolean;
  battery: number | null;
  lastReportAt: string | null;
}

/** The presence layer at a glance (mirrors AutomationService PresenceState.Snapshot). */
export interface PresenceStatus {
  anyoneHome: boolean;
  homeCount: number;
  residents: ResidentStatus[];
}

export const presenceApi = {
  getResidents: (): Promise<Resident[]> =>
    apiClient.get<Resident[]>('/api/residents').then((r) => r.data),

  createResident: (body: ResidentInput): Promise<Resident> =>
    apiClient.post<Resident>('/api/residents', body).then((r) => r.data),

  updateResident: (id: string, body: ResidentInput): Promise<void> =>
    apiClient.put(`/api/residents/${encodeURIComponent(id)}`, body).then(() => undefined),

  deleteResident: (id: string): Promise<void> =>
    apiClient.delete(`/api/residents/${encodeURIComponent(id)}`).then(() => undefined),

  getSettings: (): Promise<PresenceSettings> =>
    apiClient.get<PresenceSettings>('/api/settings/presence').then((r) => r.data),

  saveSettings: (body: {
    homeRadiusMeters: number;
    awayGraceSeconds: number;
    ownTracksToken: string | null;
  }): Promise<PresenceSettings> =>
    apiClient.put<PresenceSettings>('/api/settings/presence', body).then((r) => r.data),

  // Live home/away per resident + the aggregate (from the AutomationService presence layer).
  getStatus: (): Promise<PresenceStatus> =>
    apiClient.get<PresenceStatus>('/api/presence/status').then((r) => r.data),
};
