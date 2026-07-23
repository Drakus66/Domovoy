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
import { getAccessToken } from './auth';

// Strip a trailing slash so a relative base of '/' yields '/hub/devices', not '//hub/devices'
// (a '//…' URL is protocol-relative and would resolve the host as 'hub').
const API_BASE = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
export const HUB_URL = `${API_BASE}/hub/devices`;
// Notifications ride a dedicated hub (2M.2): the device hub broadcasts the whole state firehose to every
// connection, so a banner-only client on it would receive (and warn about) every device update. This hub
// only ever pushes NotificationRaised, so the notification listener stays quiet.
export const NOTIFICATION_HUB_URL = `${API_BASE}/hub/notifications`;

// Capped backoff used both for the initial connect retry and for automatic reconnects:
// 0s, 2s, 5s, then 10s forever. The dashboard must stay live across gateway restarts/blips,
// so reconnection never gives up (unlike the SignalR default, which stops after ~42s).
const BACKOFF_MS = [0, 2_000, 5_000, 10_000];
const backoffFor = (attempt: number) => BACKOFF_MS[Math.min(attempt, BACKOFF_MS.length - 1)];

const reconnectPolicy: IRetryPolicy = {
  nextRetryDelayInMilliseconds: (ctx: RetryContext) => backoffFor(ctx.previousRetryCount),
};

/** Build a hub connection to <paramref name="url"/> with indefinite, capped-backoff automatic reconnect. */
function buildHubConnection(url: string): HubConnection {
  return new HubConnectionBuilder()
    // A WebSocket can't carry an Authorization header, so SignalR sends the token as ?access_token= on /hub/*;
    // the gateway lifts it back out (JwtBearerEvents.OnMessageReceived). accessTokenFactory is re-invoked on each
    // (re)connect, so a token refreshed mid-session is picked up automatically. Empty when auth is off.
    .withUrl(url, { accessTokenFactory: () => getAccessToken() ?? '' })
    .withAutomaticReconnect(reconnectPolicy)
    .configureLogging(LogLevel.Warning)
    .build();
}

/** Build the device-state hub connection (real-time device/zigbee updates). */
export function buildDeviceHubConnection(): HubConnection {
  return buildHubConnection(HUB_URL);
}

/** Build the notifications-only hub connection (2M.2 in-app banners). */
export function buildNotificationHubConnection(): HubConnection {
  return buildHubConnection(NOTIFICATION_HUB_URL);
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
