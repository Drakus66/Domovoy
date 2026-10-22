// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** Notification delivery channel status (matches AutomationService, Epic 2G). */
export interface NotificationChannels {
  all: string[];
  enabled: string[];
}

/** Result of sending a test notification through the enabled channels. */
export interface NotificationTestResult {
  delivered: number;
  enabled: string[];
}

/** An actionable-notification button (Epic 3F). Server-side kinds execute a command with actor attribution. */
export interface NotificationAction {
  id: string;
  label: string;
  kind: string;
  params?: Record<string, string>;
}

/** The notification categories (Epic 3F taxonomy) — the rows of the routing matrix. */
export const NOTIFICATION_CATEGORIES = ['reactive', 'proactive', 'optimization'] as const;

/** Notification-discipline settings (Epic 3F): per-category channel routing (opt-out), rate-limit, safety floor. */
export interface NotificationSettings {
  mutedChannels: Record<string, string[]>;
  minIntervalSeconds: Record<string, number>;
  safetyFloorEnabled: boolean;
}

/**
 * Client for notifications. Channels (telegram, webhook/push) are configured on the server (env-gated, off by
 * default); the UI shows which are enabled, sends a test message, edits the Epic 3F discipline settings, and
 * executes an actionable button.
 */
export const notificationsApi = {
  getChannels: (): Promise<NotificationChannels> =>
    apiClient.get<NotificationChannels>('/api/notifications/channels').then((r) => r.data),

  sendTest: (): Promise<NotificationTestResult> =>
    apiClient.post<NotificationTestResult>('/api/notifications/test').then((r) => r.data),

  getSettings: (): Promise<NotificationSettings> =>
    apiClient.get<NotificationSettings>('/api/settings/notifications').then((r) => r.data),

  saveSettings: (settings: NotificationSettings): Promise<NotificationSettings> =>
    apiClient.put<NotificationSettings>('/api/settings/notifications', settings).then((r) => r.data),

  executeAction: (action: NotificationAction): Promise<void> =>
    apiClient.post('/api/notifications/action', action).then(() => undefined),
};
