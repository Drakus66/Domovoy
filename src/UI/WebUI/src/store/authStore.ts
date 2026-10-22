// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { create } from 'zustand';
import {
  authApi,
  clearSession,
  onAuthLost,
  setSession,
  type AuthUserInfo,
} from '../api/auth';
import { setCurrentUserId } from '../api/currentUser';

/**
 * Auth store (mobile-app / remote-access track). Drives the top-level gate: on boot it asks the gateway who we
 * are (GET /api/auth/me). With auth OFF the gateway answers with a synthetic admin, so `status` becomes
 * `authenticated` and the login screen is never shown — dev behaves exactly as before. With auth ON, an expired
 * token is refreshed by the axios interceptor during that call; a hard 401 (no/!refreshable token) leaves us
 * `unauthenticated` and the app renders the login screen instead of the routes.
 */

export type AuthStatus = 'loading' | 'authenticated' | 'unauthenticated';

interface AuthStore {
  status: AuthStatus;
  user: AuthUserInfo | null;

  bootstrap: () => Promise<void>;
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  hasPermission: (permission: string) => boolean;
}

const SYSTEM_ADMIN = 'system.admin';

export const useAuthStore = create<AuthStore>((set, get) => {
  // When a refresh finally fails mid-session, drop straight to the login screen.
  onAuthLost(() => {
    setCurrentUserId(null);
    set({ status: 'unauthenticated', user: null });
  });

  return {
    status: 'loading',
    user: null,

    bootstrap: async () => {
      try {
        const user = await authApi.me();
        // Keep the attribution header in sync with the signed-in identity.
        setCurrentUserId(user.id && user.id !== 'dev-admin' ? user.id : null);
        set({ status: 'authenticated', user });
      } catch {
        clearSession();
        set({ status: 'unauthenticated', user: null });
      }
    },

    login: async (username: string, password: string) => {
      const resp = await authApi.login(username, password);
      setSession(resp);
      setCurrentUserId(resp.user.id && resp.user.id !== 'dev-admin' ? resp.user.id : null);
      set({ status: 'authenticated', user: resp.user });
    },

    logout: async () => {
      await authApi.logout();
      clearSession();
      setCurrentUserId(null);
      set({ status: 'unauthenticated', user: null });
    },

    hasPermission: (permission: string): boolean => {
      const user = get().user;
      if (!user) return false;
      return user.permissions.includes(permission) || user.permissions.includes(SYSTEM_ADMIN);
    },
  };
});
