// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import axios from 'axios';
// Static import despite the client ↔ auth cycle: client.ts uses this module's helpers only inside its
// interceptor callbacks, and we use `apiClient` only inside the authApi functions below — neither touches the
// other at module-evaluation time, so the live bindings are always resolved by the time they're called.
import apiClient from './client';

/**
 * Local authentication (mobile-app / remote-access track): token storage, the auth API calls, and the
 * single-flight refresh used by the axios 401 interceptor. Kept in its own module (not the axios client) so the
 * interceptor and the auth store can both use it without a circular import.
 *
 * When the gateway runs with auth OFF (dev), login/me return a synthetic admin with EMPTY tokens; an empty token
 * is treated as "no token" everywhere here, so the app behaves as always-signed-in and never sends an
 * Authorization header.
 */

export interface AuthUserInfo {
  id: string;
  username: string;
  displayName: string;
  email?: string | null;
  roleIds: string[];
  permissions: string[];
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: AuthUserInfo;
}

// Same base the axios client uses (same-origin '/' in the nginx build, localhost:5000 in dev).
export const API_BASE = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');

const ACCESS_KEY = 'domovoy.auth.access';
const REFRESH_KEY = 'domovoy.auth.refresh';

const read = (key: string): string | null => {
  try {
    const v = localStorage.getItem(key);
    return v && v.length > 0 ? v : null;
  } catch {
    return null;
  }
};

export const getAccessToken = (): string | null => read(ACCESS_KEY);
export const getRefreshToken = (): string | null => read(REFRESH_KEY);

export const setSession = (resp: LoginResponse): void => {
  try {
    // Empty tokens (auth disabled) → clear, so no stale Authorization header is sent.
    if (resp.accessToken) localStorage.setItem(ACCESS_KEY, resp.accessToken);
    else localStorage.removeItem(ACCESS_KEY);
    if (resp.refreshToken) localStorage.setItem(REFRESH_KEY, resp.refreshToken);
    else localStorage.removeItem(REFRESH_KEY);
  } catch {
    // Storage unavailable (private mode): the session lives only in memory for this tab.
  }
};

export const clearSession = (): void => {
  try {
    localStorage.removeItem(ACCESS_KEY);
    localStorage.removeItem(REFRESH_KEY);
  } catch {
    // ignore
  }
};

// Callback the auth store registers so the 401 interceptor can flip the app to "unauthenticated" when a refresh
// finally fails, without this module importing the store.
let authLostHandler: (() => void) | null = null;
export const onAuthLost = (handler: () => void): void => {
  authLostHandler = handler;
};
export const notifyAuthLost = (): void => {
  clearSession();
  authLostHandler?.();
};

// Single-flight refresh: many requests can 401 at once (token just expired); they must share one refresh call,
// not stampede the endpoint. Uses a bare axios (not the intercepted client) to avoid recursion.
let refreshing: Promise<boolean> | null = null;
export const tryRefresh = (): Promise<boolean> => {
  if (refreshing) return refreshing;

  refreshing = (async () => {
    const refreshToken = getRefreshToken();
    if (!refreshToken) return false;
    try {
      const resp = await axios.post<LoginResponse>(`${API_BASE}/api/auth/refresh`, { refreshToken });
      setSession(resp.data);
      return true;
    } catch {
      clearSession();
      return false;
    }
  })();

  return refreshing.finally(() => {
    refreshing = null;
  });
};

export const authApi = {
  login: (username: string, password: string): Promise<LoginResponse> =>
    axios.post<LoginResponse>(`${API_BASE}/api/auth/login`, { username, password }).then((r) => r.data),

  // Uses the intercepted client so a valid stored token is attached (and refreshed on 401) automatically.
  me: (): Promise<AuthUserInfo> =>
    apiClient.get<AuthUserInfo>('/api/auth/me').then((r) => r.data),

  logout: (): Promise<void> =>
    apiClient.post('/api/auth/logout').then(() => undefined).catch(() => undefined),

  changePassword: (currentPassword: string, newPassword: string): Promise<void> =>
    apiClient.post('/api/auth/change-password', { currentPassword, newPassword }).then(() => undefined),
};
