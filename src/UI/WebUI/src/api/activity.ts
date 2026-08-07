// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

export type ActivitySource = 'device' | 'automation' | 'block' | 'system';
export type ActivitySeverity = 'info' | 'warn' | 'error';

/** Coarse initiator bucket behind an activity row ("who kind of did it"). */
export type TriggerKind = 'user' | 'rule' | 'device' | 'ml' | 'block' | 'presence';

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
  /** Concrete initiator: bucket + id + server-resolved display name (clickable "by …" chip). */
  triggerKind?: TriggerKind | string | null;
  triggerId?: string | null;
  triggerName?: string | null;
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

  /**
   * How many rows match — counted in the database, nothing shipped. For a headline number, asking for
   * the rows and taking `.length` means pulling hundreds of records a minute to render one integer.
   * `severity` / `q` are not supported here (they are in-memory filters on the feed, see the endpoint).
   */
  count: (q: Omit<ActivityQuery, 'severity' | 'q' | 'limit'> = {}): Promise<number> =>
    apiClient.get<{ count: number }>('/api/activity/count', { params: params(q) }).then((r) => r.data.count),
};
