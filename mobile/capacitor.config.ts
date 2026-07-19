// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import type { CapacitorConfig } from '@capacitor/cli';

/**
 * Domovoy Android shell (mobile-app / remote-access track, Epic 2O.3). This is a thin native wrapper: it ships a
 * tiny bundled "launcher" (www/) whose only job is onboarding + the connection manager (probe LAN → IPv6-direct →
 * SSH-fallback) and then hand the WebView over to the home's own UI (the same PWA served by nginx). We do NOT
 * bundle the WebUI — the app always loads the live UI, so a UI change needs no app rebuild.
 *
 * `server.allowNavigation` is permissive ('*') because the server URL is chosen at runtime by the user (their
 * home's LAN IP / IPv6 host / VPS hostname) and can't be an app-build-time constant. Acceptable for a
 * self-hosted app the user points at their own home; tighten it if you ship a fixed hostname.
 */
const config: CapacitorConfig = {
  appId: 'com.domovoy.app',
  appName: 'Domovoy',
  webDir: 'www',
  android: {
    // Allow http:// so the LAN path (http://192.168.x.x) works without a cert; remote paths use https.
    allowMixedContent: true,
  },
  server: {
    androidScheme: 'https',
    allowNavigation: ['*'],
  },
};

export default config;
