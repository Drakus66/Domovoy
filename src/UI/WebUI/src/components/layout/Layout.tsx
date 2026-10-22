// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { Box, Toolbar } from '@mui/material';
import { Outlet } from 'react-router-dom';
import Navigation from './Navigation';
import ErrorBoundary from '../common/ErrorBoundary';
import KioskShell from '../kiosk/KioskShell';
import { useKioskStore } from '../../store/kioskStore';

function Layout() {
  const kiosk = useKioskStore((s) => s.enabled);

  // Kiosk mode (Epic 2O.2): no navigation chrome, the shell locks to the pinned dashboard and handles idle.
  if (kiosk) {
    return (
      <KioskShell>
        <ErrorBoundary>
          <Outlet />
        </ErrorBoundary>
      </KioskShell>
    );
  }

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Navigation />
      <Box component="main" sx={{ flexGrow: 1, minWidth: 0 }}>
        {/* Spacer to clear the fixed mobile AppBar (no-op on desktop sidebar layout). */}
        <Toolbar sx={{ display: { xs: 'block', md: 'none' } }} />
        <ErrorBoundary>
          <Outlet />
        </ErrorBoundary>
      </Box>
    </Box>
  );
}

export default Layout;
