// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import {
  HubConnection,
  HubConnectionBuilder,
  IRetryPolicy,
  LogLevel,
  RetryContext,
} from '@microsoft/signalr';

// Strip a trailing slash so a relative base of '/' yields '/hub/devices', not '//hub/devices'
// (a '//…' URL is protocol-relative and would resolve the host as 'hub').
const API_BASE = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
export const HUB_URL = `${API_BASE}/hub/devices`;

// Capped backoff used both for the initial connect retry and for automatic reconnects:
// 0s, 2s, 5s, then 10s forever. The dashboard must stay live across gateway restarts/blips,
// so reconnection never gives up (unlike the SignalR default, which stops after ~42s).
const BACKOFF_MS = [0, 2_000, 5_000, 10_000];
const backoffFor = (attempt: number) => BACKOFF_MS[Math.min(attempt, BACKOFF_MS.length - 1)];

const reconnectPolicy: IRetryPolicy = {
  nextRetryDelayInMilliseconds: (ctx: RetryContext) => backoffFor(ctx.previousRetryCount),
};

/** Build the device-hub connection with indefinite, capped-backoff automatic reconnect. */
export function buildDeviceHubConnection(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(HUB_URL)
    .withAutomaticReconnect(reconnectPolicy)
    .configureLogging(LogLevel.Warning)
    .build();
}

/**
 * Start a hub connection, retrying indefinitely until it succeeds or <paramref name="isCancelled"/>
 * reports the consumer has gone away (component unmounted). This covers the gap that
 * withAutomaticReconnect() does NOT: a failed *initial* connect (e.g. gateway still starting),
 * which otherwise leaves the UI stuck on REST polling until a manual refresh.
 */
export async function startDeviceHub(conn: HubConnection, isCancelled: () => boolean): Promise<void> {
  let attempt = 0;
  while (!isCancelled()) {
    try {
      await conn.start();
      return;
    } catch (err) {
      const delay = backoffFor(attempt++);
      console.warn(`[deviceHub] connect failed, retrying in ${delay}ms`, err);
      await new Promise((resolve) => setTimeout(resolve, delay));
    }
  }
}
