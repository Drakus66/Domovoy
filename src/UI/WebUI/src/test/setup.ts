import { expect, afterEach, beforeAll, afterAll, vi } from 'vitest';
import { cleanup } from '@testing-library/react';
import '@testing-library/jest-dom';
import { setupServer } from 'msw/node';
import { http, HttpResponse } from 'msw';

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

