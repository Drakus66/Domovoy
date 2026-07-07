// Shared i18n configuration used by both the runtime init (index.ts, http-backend)
// and the test init (src/test/setup.ts, inline resources). Keeping the namespace
// list and defaults here means the two init paths can never drift apart.

import type { InitOptions } from 'i18next';
import { DEFAULT_LANGUAGE, supportedLngs } from './languages';

/**
 * One namespace per page plus a shared `common` (app chrome, actions, statuses,
 * shared components) and `nav` (sidebar). Pages lazy-load their own namespace via
 * useTranslation('<ns>'); common + nav are preloaded so the frame renders at once.
 */
export const namespaces = [
  'common',
  'nav',
  'devices',
  'dashboards',
  'zones',
  'modes',
  'automations',
  'flow',
  'blocks',
  'models',
  'proposals',
  'users',
  'plugins',
  'logs',
  'status',
  'zigbee',
] as const;

export const defaultNS = 'common';

export const preloadNS = ['common', 'nav'];

export const baseOptions: InitOptions = {
  fallbackLng: DEFAULT_LANGUAGE,
  supportedLngs,
  ns: preloadNS,
  defaultNS,
  interpolation: { escapeValue: false }, // React already escapes
};
