// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/**
 * Delivery and updates (roadmap Epic 3K). Everything here goes through the api-gateway, which is
 * where authentication and the `system.admin` permission live — the delivery service itself is not
 * reachable from the browser.
 */

/** Which channel this house is subscribed to, and how it polls. */
export interface UpdateSettings {
  id: string;
  /** `release` — stable builds from master; `dev` — fresh builds from develop. */
  channel: 'release' | 'dev';
  checkEnabled: boolean;
  checkIntervalHours: number;
  /** Take a backup before applying. On by default. */
  backupBeforeUpdate: boolean;
  lastCheckAt: string | null;
  lastCheckResult: 'ok' | 'error' | null;
  updatedAt: string;
}

/** Declared compatibility of a component, straight from its image label. */
export interface ComponentDeps {
  component: string;
  version: string;
  bus: { speaks: number; understands: number } | null;
  provides: Record<string, { version: number; minCompat: number }>;
  requires: Record<string, number>;
}

/** One row of the components table: what runs now versus what the channel offers. */
export interface ComponentStatus {
  name: string;
  installed: string | null;
  digest: string | null;
  available: string | null;
  hasUpdate: boolean;
  deps: ComponentDeps | null;
}

export interface ComponentsResponse {
  channel: string;
  components: ComponentStatus[];
}

/** One component's move, with the reason it is in the plan at all. */
export interface PlannedMove {
  component: string;
  container: string;
  from: string;
  to: string;
  /** Why this is included — shown verbatim, e.g. "automation-service требует db-api ≥ 5". */
  reason: string;
}

/** Components that must move together because the intermediate state would be incompatible. */
export interface PlanGroup {
  atomic: boolean;
  members: PlannedMove[];
}

export interface UpdatePlan {
  channel: string;
  ok: boolean;
  /** Set when no combination of available versions satisfies the constraints. */
  refusal: string | null;
  /** Components named in the refusal. */
  conflict: string[];
  empty: boolean;
  groups: PlanGroup[];
}

export interface UpdateStep {
  name: string;
  status: 'pending' | 'running' | 'ok' | 'error' | 'skipped';
  at: string;
  detail: string | null;
}

export interface UpdateRun {
  id: string;
  startedAt: string;
  finishedAt: string | null;
  status: 'idle' | 'running' | 'ok' | 'error' | 'rolled-back';
  channel: string;
  error: string | null;
  steps: UpdateStep[];
  plan: PlannedMove[];
  backupFile: string | null;
  topologyFrom: number | null;
  topologyTo: number | null;
  /** Keys the release added to the host .env — worth showing, the owner may want to fill them in. */
  envKeysAdded: string[];
}

export const updatesApi = {
  getSettings: (): Promise<UpdateSettings> =>
    apiClient.get<UpdateSettings>('/api/updates/settings').then((r) => r.data),

  saveSettings: (body: Partial<Pick<UpdateSettings,
    'channel' | 'checkEnabled' | 'checkIntervalHours' | 'backupBeforeUpdate'>>): Promise<UpdateSettings> =>
    apiClient.put<UpdateSettings>('/api/updates/settings', body).then((r) => r.data),

  components: (): Promise<ComponentsResponse> =>
    apiClient.get<ComponentsResponse>('/api/updates/components', { timeout: 0 }).then((r) => r.data),

  // Walks every component's manifest in the registry — slow on a home connection, no client timeout.
  check: (): Promise<{ channel: string; outdated: string[]; checkedAt: string }> =>
    apiClient.get('/api/updates/check', { timeout: 0 }).then((r) => r.data),

  /** What updating these would actually do. Always called before apply, so the dialog can show it. */
  plan: (components: string[] | null): Promise<UpdatePlan> =>
    apiClient
      .post<UpdatePlan>('/api/updates/plan', { components, all: components === null }, { timeout: 0 })
      .then((r) => r.data),

  apply: (components: string[] | null, backup: boolean): Promise<unknown> =>
    apiClient
      .post('/api/updates/apply', { components, all: components === null, backup }, { timeout: 0 })
      .then((r) => r.data),

  // Polled, not pushed: an update recreates the gateway and the UI near the end, so the connection
  // is expected to drop. The run state lives in a file on the host and survives that.
  status: (): Promise<UpdateRun | { status: 'idle' }> =>
    apiClient.get<UpdateRun | { status: 'idle' }>('/api/updates/status').then((r) => r.data),

  history: (): Promise<UpdateRun[]> =>
    apiClient.get<UpdateRun[]>('/api/updates/history').then((r) => r.data),

  rollback: (): Promise<unknown> =>
    apiClient.post('/api/updates/rollback', null, { timeout: 0 }).then((r) => r.data),
};
