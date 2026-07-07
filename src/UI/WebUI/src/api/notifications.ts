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

/**
 * Client for notification delivery channels (Epic 2G). Channels (telegram, webhook/push) are configured on
 * the server (env-gated, off by default); the UI can only show which are enabled and send a test message.
 */
export const notificationsApi = {
  getChannels: (): Promise<NotificationChannels> =>
    apiClient.get<NotificationChannels>('/api/notifications/channels').then((r) => r.data),

  sendTest: (): Promise<NotificationTestResult> =>
    apiClient.post<NotificationTestResult>('/api/notifications/test').then((r) => r.data),
};
