// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { create } from 'zustand';
import i18n from 'i18next';
import {
  dashboardsApi,
  type Dashboard,
  type DashboardInput,
} from '../api/dashboards';
import { useUIStore } from './uiStore';

/**
 * Shared cache of custom dashboard tabs + the hidden-spheres preference, so the tab strip and the
 * editor dialog see one copy and switching tabs never refetches. Mutations are optimistic where
 * cheap (delete/reorder/prefs) and confirmed-by-response where the server assigns fields (create).
 */
interface DashboardStore {
  dashboards: Dashboard[];
  hiddenSpheres: string[];
  loaded: boolean;

  load: () => Promise<void>;
  create: (input: DashboardInput) => Promise<Dashboard | null>;
  update: (id: string, input: DashboardInput) => Promise<boolean>;
  remove: (id: string) => Promise<boolean>;
  reorder: (ids: string[]) => Promise<void>;
  setHiddenSpheres: (hidden: string[]) => Promise<void>;
}

const notifyError = (key: string) =>
  useUIStore.getState().showNotification('error', i18n.t(`dashboards:errors.${key}`));

export const useDashboardStore = create<DashboardStore>((set, get) => ({
  dashboards: [],
  hiddenSpheres: [],
  loaded: false,

  load: async () => {
    try {
      const [dashboards, prefs] = await Promise.all([
        dashboardsApi.list(),
        dashboardsApi.getPrefs(),
      ]);
      set({ dashboards, hiddenSpheres: prefs.hiddenSpheres, loaded: true });
    } catch {
      // Keep whatever we had; the page stays usable with just All + spheres.
      set({ loaded: true });
      notifyError('load');
    }
  },

  create: async (input) => {
    try {
      const created = await dashboardsApi.create(input);
      set((s) => ({ dashboards: [...s.dashboards, created] }));
      return created;
    } catch {
      notifyError('save');
      return null;
    }
  },

  update: async (id, input) => {
    const before = get().dashboards;
    set((s) => ({
      dashboards: s.dashboards.map((d) =>
        d.id === id ? { ...d, name: input.name, icon: input.icon ?? null, sections: input.sections } : d),
    }));
    try {
      await dashboardsApi.update(id, input);
      return true;
    } catch {
      set({ dashboards: before });
      notifyError('save');
      return false;
    }
  },

  remove: async (id) => {
    const before = get().dashboards;
    set((s) => ({ dashboards: s.dashboards.filter((d) => d.id !== id) }));
    try {
      await dashboardsApi.remove(id);
      return true;
    } catch {
      set({ dashboards: before });
      notifyError('delete');
      return false;
    }
  },

  reorder: async (ids) => {
    const before = get().dashboards;
    const byId = new Map(before.map((d) => [d.id, d]));
    const ordered = ids.flatMap((id) => (byId.has(id) ? [byId.get(id)!] : []));
    set({ dashboards: ordered.map((d, i) => ({ ...d, order: i })) });
    try {
      await dashboardsApi.reorder(ids);
    } catch {
      set({ dashboards: before });
      notifyError('save');
    }
  },

  setHiddenSpheres: async (hidden) => {
    const before = get().hiddenSpheres;
    set({ hiddenSpheres: hidden });
    try {
      await dashboardsApi.savePrefs(hidden);
    } catch {
      set({ hiddenSpheres: before });
      notifyError('save');
    }
  },
}));
