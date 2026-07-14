// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../../test/setup';
import { useAggregateSeries } from './useAggregateSeries';
import { __resetTelemetryBatch } from './useTelemetryBatch';

const twoBuckets = [
  { timestamp: '2026-07-14T00:00:00Z', value: 20, min: 20, max: 20, avg: 20, count: 1 },
  { timestamp: '2026-07-14T01:00:00Z', value: 22, min: 22, max: 22, avg: 22, count: 1 },
];

interface BatchBody { series: { deviceId: string; capabilityId: string }[]; }

describe('useAggregateSeries (chart data source)', () => {
  beforeEach(() => __resetTelemetryBatch());

  it('coalesces device-scoped charts into one batch request', async () => {
    const counter = { calls: 0 };
    server.use(http.post('*/api/telemetry/aggregate/batch', async ({ request }) => {
      counter.calls += 1;
      const body = (await request.json()) as BatchBody;
      return HttpResponse.json(body.series.map((s) => ({ ...s, buckets: twoBuckets })));
    }));

    const a = renderHook(() => useAggregateSeries({ capabilityId: 'temperature', deviceId: 'd1', hours: 24, bucket: 'hour' }));
    const b = renderHook(() => useAggregateSeries({ capabilityId: 'power', deviceId: 'd2', hours: 24, bucket: 'hour' }));

    await waitFor(() => {
      expect(a.result.current.buckets).not.toBeNull();
      expect(b.result.current.buckets).not.toBeNull();
    });

    expect(counter.calls).toBe(1); // two charts, one round-trip
    expect(a.result.current.buckets).toHaveLength(2);
    expect(a.result.current.error).toBe(false);
  });

  it('falls back to the direct aggregate endpoint for a zone-scoped series', async () => {
    let batchCalled = false;
    let aggregateCalled = false;
    server.use(
      http.post('*/api/telemetry/aggregate/batch', () => { batchCalled = true; return HttpResponse.json([]); }),
      http.get('*/api/telemetry/aggregate', () => { aggregateCalled = true; return HttpResponse.json(twoBuckets); }),
    );

    const h = renderHook(() => useAggregateSeries({ capabilityId: 'temperature', zoneId: 'z1', hours: 24, bucket: 'hour' }));

    await waitFor(() => expect(h.result.current.buckets).toHaveLength(2));
    expect(aggregateCalled).toBe(true);
    expect(batchCalled).toBe(false);
  });
});
