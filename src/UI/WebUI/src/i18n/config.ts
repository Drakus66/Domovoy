// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

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
  'auth',
  'devices',
  'dashboards',
  'zones',
  'modes',
  'automations',
  'scenes',
  'blocks',
  'models',
  'proposals',
  'variables',
  'users',
  'plugins',
  'logs',
  'diary',
  'status',
  'zigbee',
  'kiosk',
  // Shared vocabulary of operator/choice codes (eq/gt, and/or, min/max, PID presets, …) rendered as
  // human labels. Cross-cutting (automations and blocks both use it), so preloaded rather than page-scoped.
  'operators',
] as const;

export const defaultNS = 'common';

// `auth` is preloaded because the login screen renders before any page-scoped namespace loads.
export const preloadNS = ['common', 'nav', 'operators', 'auth'];

export const baseOptions: InitOptions = {
  fallbackLng: DEFAULT_LANGUAGE,
  supportedLngs,
  ns: preloadNS,
  defaultNS,
  interpolation: { escapeValue: false }, // React already escapes
};
