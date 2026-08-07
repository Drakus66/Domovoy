// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { expect, afterEach, beforeAll, beforeEach, afterAll, vi } from 'vitest';
import { cleanup } from '@testing-library/react';
import '@testing-library/jest-dom';
import { setupServer } from 'msw/node';
import { http, HttpResponse } from 'msw';
import i18n, { type ResourceLanguage } from 'i18next';
import { initReactI18next } from 'react-i18next';
import { baseOptions } from '../i18n/config';
import { DEFAULT_LANGUAGE } from '../i18n/languages';
import { resetSharedResources } from '../store/sharedResource';
import { useAuthStore } from '../store/authStore';

// Tests can't use http-backend (no static server under jsdom), so init i18next with
// the real locale JSON loaded inline. Language is pinned to the Russian default, so
// component tests assert the strings a user actually sees.
const ruModules = import.meta.glob('../../public/locales/ru/*.json', { eager: true });
const enModules = import.meta.glob('../../public/locales/en/*.json', { eager: true });
const toResources = (mods: Record<string, unknown>): ResourceLanguage => {
  const out: ResourceLanguage = {};
  for (const [path, mod] of Object.entries(mods)) {
    const ns = path.match(/\/([^/]+)\.json$/)?.[1];
    if (ns) out[ns] = (mod as { default: object }).default;
  }
  return out;
};

const ruResources = toResources(ruModules);

i18n.use(initReactI18next).init({
  ...baseOptions,
  lng: DEFAULT_LANGUAGE,
  ns: Object.keys(ruResources),
  resources: { ru: ruResources, en: toResources(enModules) },
  initImmediate: false,
  react: { useSuspense: false },
});

// jsdom has no ResizeObserver; recharts' ResponsiveContainer (sparklines, trend charts) needs it.
if (!window.ResizeObserver) {
  window.ResizeObserver = class {
    observe() { /* no-op */ }
    unobserve() { /* no-op */ }
    disconnect() { /* no-op */ }
  };
}

// jsdom has no matchMedia; MUI's CssVarsProvider / useMediaQuery need it.
if (!window.matchMedia) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }),
  });
}

// Synthetic signed-in admin so AuthGate resolves to "authenticated" in tests (mirrors the auth-off gateway
// behaviour); system.admin implies every permission via the auth store's hasPermission fallback.
const TEST_AUTH_USER = {
  id: 'dev-admin',
  username: 'admin',
  displayName: 'Administrator (dev)',
  email: null,
  roleIds: ['admin'],
  permissions: ['system.admin'],
};

// Setup MSW server for API mocking
export const handlers = [
  // Auth (mobile-app / remote-access track): default to signed-in so the app renders its routes in tests.
  http.get('*/api/auth/me', () => HttpResponse.json(TEST_AUTH_USER)),
  http.post('*/api/auth/login', () =>
    HttpResponse.json({ accessToken: '', refreshToken: '', expiresAt: new Date().toISOString(), user: TEST_AUTH_USER })),
  http.post('*/api/auth/refresh', () =>
    HttpResponse.json({ accessToken: '', refreshToken: '', expiresAt: new Date().toISOString(), user: TEST_AUTH_USER })),
  http.post('*/api/auth/logout', () => new HttpResponse(null, { status: 204 })),
  // Default handlers - can be overridden in individual tests
  http.get('/api/devices', () => {
    return HttpResponse.json([]);
  }),
  http.get('/api/sensors', () => {
    return HttpResponse.json([]);
  }),
  // Custom dashboards: the main page loads these on every mount (wildcard host —
  // the axios client uses an absolute base URL).
  http.get('*/api/dashboards', () => {
    return HttpResponse.json([]);
  }),
  http.get('*/api/dashboards/prefs', () => {
    return HttpResponse.json({ id: 'current', hiddenSpheres: [], updatedAt: new Date().toISOString() });
  }),
];

export const server = setupServer(...handlers);

// Start server before all tests
beforeAll(() => {
  server.listen({ onUnhandledRequest: 'warn' });
});

// Start each test already signed in so AuthGate renders the routes synchronously on first paint (component
// tests use sync queries and can't wait for the async /me round-trip). The useEffect bootstrap still runs and
// re-confirms via the MSW handler above — same result.
beforeEach(() => {
  useAuthStore.setState({ status: 'authenticated', user: { ...TEST_AUTH_USER } });
});

// Reset handlers after each test
afterEach(() => {
  server.resetHandlers();
  cleanup();
  // Общие ресурсы (store/sharedResource) живут в модуле и переживают отдельный тест: без сброса
  // следующий тест увидел бы дом, оставшийся от предыдущего.
  resetSharedResources();
});

// Close server after all tests
afterAll(() => {
  server.close();
});

// Extend Vitest's expect with jest-dom matchers
expect.extend({});

