// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** One device's target state within a scene (matches Domovoy.Contracts SceneTarget, Epic 3B). */
export interface SceneTarget {
  deviceId: string;
  /** Capability id → target value, e.g. { on_off: true, brightness: 40 }. */
  set: Record<string, unknown>;
}

/** A first-class scene: a named snapshot of target capability states (matches Domovoy.Contracts Scene). */
export interface Scene {
  id: string;
  name: string;
  description?: string | null;
  icon?: string | null;
  targets: SceneTarget[];
  createdAt: string;
  updatedAt: string;
}

export type NewScene = Omit<Scene, 'id' | 'createdAt' | 'updatedAt'>;

export const scenesApi = {
  getScenes: (): Promise<Scene[]> =>
    apiClient.get<Scene[]>('/api/scenes').then((r) => r.data),

  getScene: (id: string): Promise<Scene> =>
    apiClient.get<Scene>(`/api/scenes/${encodeURIComponent(id)}`).then((r) => r.data),

  createScene: (scene: NewScene): Promise<Scene> =>
    apiClient.post<Scene>('/api/scenes', scene).then((r) => r.data),

  updateScene: (id: string, scene: Scene): Promise<void> =>
    apiClient.put(`/api/scenes/${encodeURIComponent(id)}`, scene).then(() => undefined),

  deleteScene: (id: string): Promise<void> =>
    apiClient.delete(`/api/scenes/${encodeURIComponent(id)}`).then(() => undefined),

  /** Activate a scene — fans its targets out as device commands (does not touch stored config). */
  activate: (id: string): Promise<void> =>
    apiClient.post(`/api/scenes/${encodeURIComponent(id)}/activate`).then(() => undefined),
};
