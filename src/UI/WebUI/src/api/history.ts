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
  triggerSource: string; // user | rule | device | ml | block | presence
  /** Concrete initiator id (rule/block/user/presence-sensor), when known. */
  triggerId?: string | null;
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

/** Aggregation functions the rollup endpoints accept. avg/min/max = band stats; sum = total of the
 *  bucket's samples; delta = last−first within the bucket (energy consumption from a counter, Epic 3C). */
export type AggFn = 'avg' | 'min' | 'max' | 'sum' | 'delta';

/** One aggregated time bucket (matches DbGateway AggregateBucket, Epic 1B). */
export interface AggregateBucket {
  timestamp: string;
  value: number; // the requested aggregate (see AggFn)
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
  agg?: AggFn;
}

/** One (device, capability) series requested in a batch aggregation. */
export interface SeriesSpec {
  deviceId: string;
  capabilityId: string;
}

/** Aggregated buckets for one requested series (matches DbGateway SeriesResult). */
export interface SeriesResult extends SeriesSpec {
  buckets: AggregateBucket[];
}

export interface AggregateBatchQuery {
  series: SeriesSpec[];
  from?: string;
  to?: string;
  bucket?: 'minute' | 'hour' | 'day';
  agg?: AggFn;
  /** Max points kept per series (most-recent); server default is 48. */
  maxPoints?: number;
}

/** Latest event-log row for one device (matches DbGateway LatestEventDto) — batch provenance. */
export interface LatestEvent {
  deviceId: string;
  timestamp: string;
  capabilityId: string;
  triggerSource: string;
  triggerId?: string | null;
  ruleId?: string | null;
  correlationId?: string | null;
  newValue?: unknown;
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

  /** Aggregate many (device, capability) series in one round-trip (dashboard sparklines / composed charts). */
  getAggregateBatch: (q: AggregateBatchQuery): Promise<SeriesResult[]> =>
    apiClient.post<SeriesResult[]>('/api/telemetry/aggregate/batch', q).then((r) => r.data),

  /** Latest event-log row per device in one round-trip — per-tile "last changed by …" provenance. */
  getLatestByDevice: (deviceIds: string[], window?: { from?: string; to?: string }): Promise<LatestEvent[]> =>
    apiClient.post<LatestEvent[]>('/api/events/latest-by-device', { deviceIds, ...window }).then((r) => r.data),

  /** URL for the CSV period export (Epic 1B) — open/download directly. */
  csvExportUrl: (q: HistoryQuery = {}): string => {
    const qs = new URLSearchParams({ ...params(q), format: 'csv' } as Record<string, string>).toString();
    return `${apiClient.defaults.baseURL ?? ''}/api/telemetry?${qs}`;
  },
};
