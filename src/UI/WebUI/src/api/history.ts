// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** Domain event-log record — a state delta or command (matches DbGateway EventLogDto, P0-5). */
export interface EventLogEntry {
  timestamp: string;
  deviceId: string;
  zoneId: string;
  kind: string; // state_change | command
  capabilityId: string;
  oldValue?: unknown;
  newValue?: unknown;
  triggerSource: string; // user | rule | device | ml
  ruleId?: string | null;
  decisionId?: string | null;
  mode?: string | null;
  correlationId?: string | null;
}

/** Numeric telemetry sample (matches DbGateway TelemetryDto, P0-5). */
export interface TelemetrySample {
  timestamp: string;
  deviceId: string;
  zoneId: string;
  capabilityId: string;
  unit?: string | null;
  value: number;
}

export interface HistoryQuery {
  deviceId?: string;
  capabilityId?: string;
  zoneId?: string;
  kind?: string;
  from?: string;
  to?: string;
  limit?: number;
}

/** One aggregated time bucket (matches DbGateway AggregateBucket, Epic 1B). */
export interface AggregateBucket {
  timestamp: string;
  value: number; // the requested aggregate (avg | min | max)
  min: number;
  max: number;
  avg: number;
  count: number;
}

export interface AggregateQuery {
  deviceId?: string;
  capabilityId?: string;
  zoneId?: string;
  from?: string;
  to?: string;
  bucket?: 'minute' | 'hour' | 'day';
  agg?: 'avg' | 'min' | 'max';
}

const params = (q: HistoryQuery) =>
  Object.fromEntries(Object.entries(q).filter(([, v]) => v !== undefined && v !== ''));

export const historyApi = {
  /** Device event-log history (state deltas + commands) with trigger attribution. */
  getEvents: (q: HistoryQuery = {}): Promise<EventLogEntry[]> =>
    apiClient.get<EventLogEntry[]>('/api/events', { params: params(q) }).then((r) => r.data),

  /** Numeric telemetry samples over a period. */
  getTelemetry: (q: HistoryQuery = {}): Promise<TelemetrySample[]> =>
    apiClient.get<TelemetrySample[]>('/api/telemetry', { params: params(q) }).then((r) => r.data),

  /** Aggregated telemetry rollups (minute/hour/day) for trend charts (Epic 1B). */
  getAggregate: (q: AggregateQuery = {}): Promise<AggregateBucket[]> =>
    apiClient.get<AggregateBucket[]>('/api/telemetry/aggregate', { params: params(q) }).then((r) => r.data),

  /** URL for the CSV period export (Epic 1B) — open/download directly. */
  csvExportUrl: (q: HistoryQuery = {}): string => {
    const qs = new URLSearchParams({ ...params(q), format: 'csv' } as Record<string, string>).toString();
    return `${apiClient.defaults.baseURL ?? ''}/api/telemetry?${qs}`;
  },
};
