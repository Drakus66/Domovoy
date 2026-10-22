// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { historyApi, LatestEvent } from '../../api/history';

/**
 * Coalescing "last changed by …" provenance for a single device (roadmap dashboard fill, block C).
 * Every tile calls this with its own id; requests made within a short window batch into ONE
 * `POST /api/events/latest-by-device` instead of an /api/events call per tile. Cached per id with a TTL
 * so the whole grid refreshes on a slow cadence, not once per mount. Mirrors useTelemetryBatch.
 */

const TTL_MS = 60_000;
const FLUSH_DELAY_MS = 50;

const cache = new Map<string, { ev: LatestEvent | null; at: number }>();
const listeners = new Map<string, Set<(ev: LatestEvent | null) => void>>();
const pending = new Set<string>();
const inFlight = new Set<string>();
let timer: ReturnType<typeof setTimeout> | null = null;

function notify(id: string, ev: LatestEvent | null) {
  listeners.get(id)?.forEach((fn) => fn(ev));
}

async function flush() {
  timer = null;
  if (pending.size === 0) return;
  const ids = [...pending];
  pending.clear();
  ids.forEach((id) => inFlight.add(id));

  try {
    const rows = await historyApi.getLatestByDevice(ids);
    const byId = new Map(rows.map((r) => [r.deviceId, r]));
    for (const id of ids) {
      const ev = byId.get(id) ?? null;
      cache.set(id, { ev, at: Date.now() });
      inFlight.delete(id);
      notify(id, ev);
    }
  } catch {
    // A failed fetch is not cached (so it can retry) and leaves chips off.
    for (const id of ids) {
      inFlight.delete(id);
      notify(id, null);
    }
  }
}

function request(id: string) {
  if (pending.has(id) || inFlight.has(id)) return;
  pending.add(id);
  if (timer === null) timer = setTimeout(flush, FLUSH_DELAY_MS);
}

/** Reset all module-level state — test seam only. */
export function __resetProvenance() {
  cache.clear();
  listeners.clear();
  pending.clear();
  inFlight.clear();
  if (timer !== null) { clearTimeout(timer); timer = null; }
}

/** The most recent event-log row for one device (undefined until loaded, null if it has no history). */
export function useDeviceProvenance(deviceId: string | undefined): LatestEvent | null | undefined {
  const [ev, setEv] = useState<LatestEvent | null | undefined>(undefined);

  useEffect(() => {
    if (!deviceId) { setEv(undefined); return; }

    const cb = (v: LatestEvent | null) => setEv(v);
    (listeners.get(deviceId) ?? listeners.set(deviceId, new Set()).get(deviceId)!).add(cb);

    const serve = () => {
      const fresh = cache.get(deviceId);
      if (fresh && Date.now() - fresh.at < TTL_MS) setEv(fresh.ev);
      else request(deviceId);
    };
    serve();
    // Refresh on the same slow cadence as the digest so "who changed it" stays current.
    const interval = setInterval(() => { cache.delete(deviceId); serve(); }, TTL_MS);

    return () => {
      clearInterval(interval);
      const set = listeners.get(deviceId);
      set?.delete(cb);
      if (set && set.size === 0) listeners.delete(deviceId);
    };
  }, [deviceId]);

  return ev;
}
