// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';
import { getCurrentUserId } from './currentUser';

/** Current home mode / presence context (matches DbGateway HomeState, roadmap Epic 1G). */
export interface HomeState {
  mode: string;
  source: string;
  updatedAt: string;
}

export const modeApi = {
  getMode: (): Promise<HomeState> =>
    apiClient.get<HomeState>('/api/mode').then((r) => r.data),

  /** Well-known modes for selection (open set; deployments may add their own). */
  getOptions: (): Promise<string[]> =>
    apiClient.get<string[]>('/api/mode/options').then((r) => r.data),

  /** Manual switch. The gateway persists it and broadcasts the change to the engine + event-log.
   *  The source carries the self-declared user (actor-string `user:{id}`) when one is chosen. */
  setMode: (mode: string): Promise<HomeState> => {
    const userId = getCurrentUserId();
    const source = userId ? `user:${userId}` : 'user';
    return apiClient.put<HomeState>('/api/mode', { mode, source }).then((r) => r.data);
  },
};
