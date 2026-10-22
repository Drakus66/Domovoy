// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../../test/setup';
import { useTelemetryBatch, __resetTelemetryBatch } from './useTelemetryBatch';

const twoBuckets = [
  { timestamp: '2026-01-01T00:00:00Z', value: 1, min: 1, max: 1, avg: 1, count: 1 },
  { timestamp: '2026-01-01T01:00:00Z', value: 2, min: 2, max: 2, avg: 2, count: 1 },
];

interface BatchBody { series: { deviceId: string; capabilityId: string }[]; }

/** Echo handler: returns two buckets for every requested series and counts invocations. */
function echoBatch(counter: { calls: number; lastBody?: BatchBody }) {
  return http.post('*/api/telemetry/aggregate/batch', async ({ request }) => {
    counter.calls += 1;
    const body = (await request.json()) as BatchBody;
    counter.lastBody = body;
    return HttpResponse.json(body.series.map((s) => ({ ...s, buckets: twoBuckets })));
  });
}

describe('useTelemetryBatch coalescer (dashboard fill)', () => {
  beforeEach(() => __resetTelemetryBatch());

  it('coalesces many series requested in the same tick into one batch request', async () => {
    const counter = { calls: 0 } as { calls: number; lastBody?: BatchBody };
    server.use(echoBatch(counter));

    const a = renderHook(() => useTelemetryBatch('dev-a', 'temperature'));
    const b = renderHook(() => useTelemetryBatch('dev-b', 'power'));

    await waitFor(() => {
      expect(a.result.current.buckets).not.toBeNull();
      expect(b.result.current.buckets).not.toBeNull();
    });

    expect(counter.calls).toBe(1); // both series in ONE round-trip
    expect(counter.lastBody?.series).toHaveLength(2);
    expect(a.result.current.buckets).toHaveLength(2);
    expect(a.result.current.loading).toBe(false);
  });

  it('serves a fresh key from cache without a second request', async () => {
    const counter = { calls: 0 } as { calls: number; lastBody?: BatchBody };
    server.use(echoBatch(counter));

    const first = renderHook(() => useTelemetryBatch('dev-a', 'temperature'));
    await waitFor(() => expect(first.result.current.buckets).not.toBeNull());
    expect(counter.calls).toBe(1);

    const second = renderHook(() => useTelemetryBatch('dev-a', 'temperature'));
    await waitFor(() => expect(second.result.current.buckets).toHaveLength(2));
    expect(counter.calls).toBe(1); // cache hit — no new request
  });

  it('resolves to empty buckets (hides the sparkline) when the batch fails', async () => {
    server.use(http.post('*/api/telemetry/aggregate/batch',
      () => HttpResponse.json({ error: 'boom' }, { status: 500 })));

    const h = renderHook(() => useTelemetryBatch('dev-x', 'temperature'));
    await waitFor(() => expect(h.result.current.buckets).toEqual([]));
  });

  it('is inert when disabled or missing ids', async () => {
    const counter = { calls: 0 } as { calls: number; lastBody?: BatchBody };
    server.use(echoBatch(counter));

    const off = renderHook(() => useTelemetryBatch('dev-a', 'temperature', { enabled: false }));
    const missing = renderHook(() => useTelemetryBatch(undefined, 'temperature'));

    // Give the flush window a chance to fire; neither should have requested anything.
    await new Promise((r) => setTimeout(r, 80));
    expect(counter.calls).toBe(0);
    expect(off.result.current.buckets).toBeNull();
    expect(off.result.current.loading).toBe(false);
    expect(missing.result.current.loading).toBe(false);
  });
});
