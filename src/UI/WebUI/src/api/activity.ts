// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

export type ActivitySource = 'device' | 'automation' | 'system';
export type ActivitySeverity = 'info' | 'warn' | 'error';

/** One unified activity row (matches DbGateway ActivityEntry, roadmap Epic 2G). */
export interface ActivityEntry {
  timestamp: string;
  source: ActivitySource;
  severity: ActivitySeverity;
  title: string;
  detail?: string | null;
  deviceId?: string | null;
  kind?: string | null;
  service?: string | null;
}

export interface ActivityQuery {
  source?: ActivitySource;
  severity?: ActivitySeverity;
  deviceId?: string;
  q?: string;
  from?: string;
  to?: string;
  limit?: number;
}

const params = (q: ActivityQuery) =>
  Object.fromEntries(Object.entries(q).filter(([, v]) => v !== undefined && v !== ''));

export const activityApi = {
  /** Unified, filterable feed of device events + automation runs + system logs. */
  get: (q: ActivityQuery = {}): Promise<ActivityEntry[]> =>
    apiClient.get<ActivityEntry[]>('/api/activity', { params: params(q) }).then((r) => r.data),
};
