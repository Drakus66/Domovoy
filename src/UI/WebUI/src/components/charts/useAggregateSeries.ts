// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { historyApi, AggregateBucket } from '../../api/history';
import { useTelemetryBatch } from './useTelemetryBatch';

// A single aggregated telemetry series for a trend chart. Device-scoped series flow through the
// coalescing batch loader (so several charts on one page — a dashboard of ChartTiles, the drawer trends —
// share one round-trip); zone-scoped series, which the batch endpoint doesn't cover, fall back to the
// direct aggregate endpoint. Charts keep the full resolution (no sparkline point cap).
const CHART_MAX_POINTS = 2000;

export interface AggregateSeriesParams {
  capabilityId: string;
  deviceId?: string;
  zoneId?: string;
  hours: number;
  bucket: 'minute' | 'hour' | 'day';
}

export function useAggregateSeries(
  { capabilityId, deviceId, zoneId, hours, bucket }: AggregateSeriesParams,
): { buckets: AggregateBucket[] | null; error: boolean } {
  // Device series (no zone): coalesced batch. Zone series: direct fetch below.
  const viaBatch = !!deviceId && !zoneId;
  const batch = useTelemetryBatch(deviceId, capabilityId, {
    hours, bucket, agg: 'avg', maxPoints: CHART_MAX_POINTS, enabled: viaBatch,
  });

  const [zoneData, setZoneData] = useState<AggregateBucket[] | null>(null);
  const [zoneError, setZoneError] = useState(false);

  useEffect(() => {
    if (viaBatch) return;
    let cancelled = false;
    setZoneData(null);
    setZoneError(false);
    const from = new Date(Date.now() - hours * 3600 * 1000).toISOString();
    historyApi.getAggregate({ capabilityId, deviceId, zoneId, from, bucket, agg: 'avg' })
      .then((rows) => { if (!cancelled) setZoneData(rows); })
      .catch(() => { if (!cancelled) setZoneError(true); });
    return () => { cancelled = true; };
  }, [viaBatch, capabilityId, deviceId, zoneId, hours, bucket]);

  return viaBatch
    ? { buckets: batch.buckets, error: false }
    : { buckets: zoneData, error: zoneError };
}
