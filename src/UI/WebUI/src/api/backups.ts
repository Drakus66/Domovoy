// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** Backup schedule + retention settings (roadmap Epic 3A) — the `backup_settings` singleton. */
export interface BackupSettings {
  id: string;
  enabled: boolean;
  /** Daily run time, HH:mm, site-local. */
  time: string;
  keepCount: number;
  lastRunAt: string | null;
  lastResult: 'ok' | 'error' | null;
  lastError: string | null;
  lastFile: string | null;
  updatedAt: string;
}

/** One bundle on disk. `valid` is false when the manifest is unreadable (corrupt/foreign zip). */
export interface BackupListItem {
  fileName: string;
  sizeBytes: number;
  createdAt: string;
  reason: string | null;
  collections: number | null;
  documents: number | null;
  valid: boolean;
}

export interface BackupRunResult {
  file: string;
  sizeBytes: number;
  collections: number;
  documents: number;
}

export interface RestoreResult {
  restored: string;
  collections: number;
  documents: number;
  pluginSettings: number;
  extrasStagingDirectory: string | null;
  /** True when the stack restart broadcast went out — the UI should expect a short outage. */
  restarting: boolean;
}

export const backupsApi = {
  getSettings: (): Promise<BackupSettings> =>
    apiClient.get<BackupSettings>('/api/backup/settings').then((r) => r.data),

  saveSettings: (body: { enabled: boolean; time: string; keepCount: number }): Promise<BackupSettings> =>
    apiClient.put<BackupSettings>('/api/backup/settings', body).then((r) => r.data),

  list: (): Promise<BackupListItem[]> =>
    apiClient.get<BackupListItem[]>('/api/backup').then((r) => r.data),

  // Dumping every collection can take a while on a long history — no client-side timeout.
  runNow: (): Promise<BackupRunResult> =>
    apiClient.post<BackupRunResult>('/api/backup/run', null, { timeout: 0 }).then((r) => r.data),

  restore: (file: string): Promise<RestoreResult> =>
    apiClient
      .post<RestoreResult>(`/api/backup/${encodeURIComponent(file)}/restore`, null, { timeout: 0 })
      .then((r) => r.data),

  remove: (file: string): Promise<void> =>
    apiClient.delete(`/api/backup/${encodeURIComponent(file)}`).then(() => undefined),

  // Downloads go through a plain link (same pattern as the telemetry CSV export).
  downloadUrl: (file: string): string =>
    `${apiClient.defaults.baseURL ?? ''}/api/backup/${encodeURIComponent(file)}/download`,

  // Host migration: send the bundle as the raw request body.
  upload: (file: File): Promise<{ file: string }> =>
    apiClient
      .post<{ file: string }>('/api/backup/upload', file, {
        headers: { 'Content-Type': 'application/zip' },
        timeout: 0,
      })
      .then((r) => r.data),
};
