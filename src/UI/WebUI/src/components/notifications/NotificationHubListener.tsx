// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect } from 'react';
import { buildDeviceHubConnection, startDeviceHub } from '../../api/deviceHub';
import { useUIStore, type NotificationType } from '../../store/uiStore';

/**
 * Global LAN notification listener (2M.2). Holds one persistent DeviceHub connection for the whole app (separate
 * from the per-page device-state connection) and turns each relayed <c>NotificationRaised</c> event into an
 * in-app banner via the existing UI notification store. Rendered once, high in the tree, so a banner shows on any
 * page. Renders nothing.
 */
interface RaisedNotification {
  title: string;
  body: string;
  severity: string;
  category?: string | null;
  raisedAt: string;
}

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
    const conn = buildDeviceHubConnection();

    conn.on('NotificationRaised', (n: RaisedNotification) => {
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
