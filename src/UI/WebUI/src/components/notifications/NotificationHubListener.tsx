// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect } from 'react';
import { buildNotificationHubConnection, startDeviceHub } from '../../api/deviceHub';
import { useUIStore, type NotificationType } from '../../store/uiStore';

/**
 * Global LAN notification listener (2M.2). Holds one persistent connection to the dedicated notifications hub for
 * the whole app and turns each relayed <c>NotificationRaised</c> event into an in-app banner via the UI
 * notification store. Rendered once, high in the tree, so a banner shows on any page. Renders nothing.
 *
 * Only *important* notifications pop a banner — see {@link BANNER_SEVERITIES}. Routine info-level ones are not
 * shown as a banner (the full history lives in the Activity journal); banners are reserved for things that
 * warrant interrupting the user.
 */
interface RaisedNotification {
  title: string;
  body: string;
  severity: string;
  category?: string | null;
  raisedAt: string;
}

// Severities that warrant an interruptive banner. Everything below (info, unknown) is journal-only.
const BANNER_SEVERITIES = new Set(['warning', 'critical']);

const typeForSeverity = (severity: string): NotificationType => {
  switch (severity?.toLowerCase()) {
    case 'critical':
      return 'error';
    case 'warning':
      return 'warning';
    default:
      return 'info';
  }
};

export default function NotificationHubListener() {
  const showNotification = useUIStore((s) => s.showNotification);

  useEffect(() => {
    let cancelled = false;
    const conn = buildNotificationHubConnection();

    conn.on('NotificationRaised', (n: RaisedNotification) => {
      // Reserve banners for important notifications; info-level ones are recorded in the journal only.
      if (!BANNER_SEVERITIES.has(n.severity?.toLowerCase())) return;
      const message = n.body ? `${n.title} — ${n.body}` : n.title;
      // Critical alerts stay until dismissed (duration 0); the rest auto-dismiss.
      const duration = n.severity?.toLowerCase() === 'critical' ? 0 : 8000;
      showNotification(typeForSeverity(n.severity), message, duration);
    });

    // Keep the connection alive across gateway blips (same policy as the device-state hub).
    conn.onclose(() => { if (!cancelled) startDeviceHub(conn, () => cancelled); });
    startDeviceHub(conn, () => cancelled);

    return () => { cancelled = true; conn.stop(); };
  }, [showNotification]);

  return null;
}
