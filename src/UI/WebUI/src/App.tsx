// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { Suspense } from 'react';
import { Experimental_CssVarsProvider as CssVarsProvider } from '@mui/material/styles';
import CssBaseline from '@mui/material/CssBaseline';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { getTheme } from './theme';
import { useThemeStore } from './store/themeStore';
import { Loading } from './components/common';
import AuthGate from './components/auth/AuthGate';
import NotificationHubListener from './components/notifications/NotificationHubListener';
import Layout from './components/layout/Layout';
import Devices from './pages/Devices';
import DeviceRegistry from './pages/DeviceRegistry';
import Zones from './pages/Zones';
import Modes from './pages/Modes';
import Automations from './pages/Automations';
import Scenes from './pages/Scenes';
import Blocks from './pages/Blocks';
import Plugins from './pages/Plugins';
import Models from './pages/Models';
import Proposals from './pages/Proposals';
import Users from './pages/Users';
import Logs from './pages/Logs';
import SystemStatus from './pages/SystemStatus';
import ZigbeeDevices from './pages/ZigbeeDevices';
import Settings from './pages/Settings';
import NotFound from './pages/NotFound';
import { NotificationContainer } from './components/common';

function App() {
  const themeId = useThemeStore((s) => s.themeId);
  return (
    <CssVarsProvider theme={getTheme(themeId)} defaultMode="dark" modeStorageKey="domovoy-color-mode">
      <CssBaseline enableColorScheme />
      {/* Suspense catches the async load of translation namespaces (http-backend). */}
      <Suspense fallback={<Loading />}>
        {/* AuthGate resolves the session before the routes mount; shows the login screen when auth is required. */}
        <AuthGate>
        {/* One persistent hub connection for LAN notification banners (2M.2), on any page. */}
        <NotificationHubListener />
        <BrowserRouter>
          <Routes>
            <Route path="/" element={<Layout />}>
              <Route index element={<Devices />} />
              {/* Deep link to a dashboard tab: sphere:<category> / dashboard id. */}
              <Route path="t/:tabId" element={<Devices />} />
              {/* The full inventory (registry) — the admin counterpart of the home screen. */}
              <Route path="devices" element={<DeviceRegistry />} />
              <Route path="zones" element={<Zones />} />
              <Route path="modes" element={<Modes />} />
              <Route path="automations" element={<Automations />} />
              <Route path="scenes" element={<Scenes />} />
              <Route path="blocks" element={<Blocks />} />
              <Route path="models" element={<Models />} />
              <Route path="proposals" element={<Proposals />} />
              <Route path="users" element={<Users />} />
              <Route path="plugins" element={<Plugins />} />
              <Route path="logs" element={<Logs />} />
              <Route path="status" element={<SystemStatus />} />
              <Route path="zigbee" element={<ZigbeeDevices />} />
              <Route path="settings" element={<Settings />} />
              <Route path="*" element={<NotFound />} />
            </Route>
          </Routes>
        </BrowserRouter>
        </AuthGate>
      </Suspense>
      <NotificationContainer />
    </CssVarsProvider>
  );
}

export default App;
