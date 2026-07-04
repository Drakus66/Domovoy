import { expect, afterEach, beforeAll, afterAll, vi } from 'vitest';
import { cleanup } from '@testing-library/react';
import '@testing-library/jest-dom';
import { setupServer } from 'msw/node';
import { http, HttpResponse } from 'msw';
import i18n, { type ResourceLanguage } from 'i18next';
import { initReactI18next } from 'react-i18next';
import { baseOptions } from '../i18n/config';
import { DEFAULT_LANGUAGE } from '../i18n/languages';

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

// Setup MSW server for API mocking
export const handlers = [
  // Default handlers - can be overridden in individual tests
  http.get('/api/devices', () => {
    return HttpResponse.json([]);
  }),
  http.get('/api/sensors', () => {
    return HttpResponse.json([]);
  }),
  http.get('/api/logs', () => {
    return HttpResponse.json({ data: [], totalCount: 0 });
  }),
];

export const server = setupServer(...handlers);

// Start server before all tests
beforeAll(() => {
  server.listen({ onUnhandledRequest: 'warn' });
});

// Reset handlers after each test
afterEach(() => {
  server.resetHandlers();
  cleanup();
});

// Close server after all tests
afterAll(() => {
  server.close();
});

// Extend Vitest's expect with jest-dom matchers
expect.extend({});

