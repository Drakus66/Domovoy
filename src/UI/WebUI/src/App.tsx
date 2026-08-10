// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { lazy, Suspense } from 'react';
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
import { NotificationContainer } from './components/common';
import ConfirmDialog from './components/common/ConfirmDialog';

// Разбиение на части по маршрутам. Главная (`Devices`) остаётся в основном пакете — с неё начинается
// любой визит. Остальное грузится по требованию: `Blocks` тянет за собой xyflow, `Settings` —
// pigeon-maps, и до правки эти библиотеки лежали в основном пакете у каждого, кто просто открыл дом.
// Загрузку прикрывает уже стоящий вокруг маршрутов <Suspense>.
const DeviceRegistry = lazy(() => import('./pages/DeviceRegistry'));
const Zones = lazy(() => import('./pages/Zones'));
const Modes = lazy(() => import('./pages/Modes'));
const Automations = lazy(() => import('./pages/Automations'));
const Scenes = lazy(() => import('./pages/Scenes'));
const Variables = lazy(() => import('./pages/Variables'));
const Blocks = lazy(() => import('./pages/Blocks'));
const Plugins = lazy(() => import('./pages/Plugins'));
const Models = lazy(() => import('./pages/Models'));
const Proposals = lazy(() => import('./pages/Proposals'));
const Users = lazy(() => import('./pages/Users'));
const Presence = lazy(() => import('./pages/Presence'));
const Logs = lazy(() => import('./pages/Logs'));
const SystemStatus = lazy(() => import('./pages/SystemStatus'));
const ZigbeeDevices = lazy(() => import('./pages/ZigbeeDevices'));
const Settings = lazy(() => import('./pages/Settings'));
const NotFound = lazy(() => import('./pages/NotFound'));


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
              <Route path="variables" element={<Variables />} />
              <Route path="blocks" element={<Blocks />} />
              <Route path="models" element={<Models />} />
              <Route path="proposals" element={<Proposals />} />
              <Route path="users" element={<Users />} />
              <Route path="presence" element={<Presence />} />
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
      <ConfirmDialog />
    </CssVarsProvider>
  );
}

export default App;
