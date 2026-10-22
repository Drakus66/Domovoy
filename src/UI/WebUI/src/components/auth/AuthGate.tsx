// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { ReactNode, useEffect } from 'react';
import { Loading } from '../common';
import Login from '../../pages/Login';
import { useAuthStore } from '../../store/authStore';

/**
 * Top-level authentication gate (mobile-app / remote-access track). On mount it asks the gateway who we are; the
 * three outcomes map straight to what renders:
 *  - `loading`         → a spinner (the /me round-trip, incl. any silent token refresh);
 *  - `unauthenticated` → the full-screen login (auth is on and we have no valid session);
 *  - `authenticated`   → the app (also the auth-off case, where /me returns a synthetic admin).
 *
 * The login has no route of its own, so the current URL is untouched — after signing in the user lands on the
 * page they originally requested.
 */
export default function AuthGate({ children }: { children: ReactNode }) {
  const status = useAuthStore((s) => s.status);
  const bootstrap = useAuthStore((s) => s.bootstrap);

  useEffect(() => {
    void bootstrap();
  }, [bootstrap]);

  if (status === 'loading') return <Loading />;
  if (status === 'unauthenticated') return <Login />;
  return <>{children}</>;
}
