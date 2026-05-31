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

const params = (q: HistoryQuery) =>
  Object.fromEntries(Object.entries(q).filter(([, v]) => v !== undefined && v !== ''));

export const historyApi = {
  /** Device event-log history (state deltas + commands) with trigger attribution. */
  getEvents: (q: HistoryQuery = {}): Promise<EventLogEntry[]> =>
    apiClient.get<EventLogEntry[]>('/api/events', { params: params(q) }).then((r) => r.data),

  /** Numeric telemetry samples over a period. */
  getTelemetry: (q: HistoryQuery = {}): Promise<TelemetrySample[]> =>
    apiClient.get<TelemetrySample[]>('/api/telemetry', { params: params(q) }).then((r) => r.data),
};
