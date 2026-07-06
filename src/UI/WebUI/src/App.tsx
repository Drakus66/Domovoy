import { Suspense } from 'react';
import { Experimental_CssVarsProvider as CssVarsProvider } from '@mui/material/styles';
import CssBaseline from '@mui/material/CssBaseline';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { getTheme } from './theme';
import { useThemeStore } from './store/themeStore';
import { Loading } from './components/common';
import Layout from './components/layout/Layout';
import Devices from './pages/Devices';
import Zones from './pages/Zones';
import Modes from './pages/Modes';
import Automations from './pages/Automations';
import Blocks from './pages/Blocks';
import Plugins from './pages/Plugins';
import Flow from './pages/Flow';
import Models from './pages/Models';
import Proposals from './pages/Proposals';
import Users from './pages/Users';
import Logs from './pages/Logs';
import SystemStatus from './pages/SystemStatus';
import ZigbeeDevices from './pages/ZigbeeDevices';
import NotFound from './pages/NotFound';
import { NotificationContainer } from './components/common';

function App() {
  const themeId = useThemeStore((s) => s.themeId);
  return (
    <CssVarsProvider theme={getTheme(themeId)} defaultMode="dark" modeStorageKey="domovoy-color-mode">
      <CssBaseline enableColorScheme />
      {/* Suspense catches the async load of translation namespaces (http-backend). */}
      <Suspense fallback={<Loading />}>
        <BrowserRouter>
          <Routes>
            <Route path="/" element={<Layout />}>
              <Route index element={<Devices />} />
              <Route path="zones" element={<Zones />} />
              <Route path="modes" element={<Modes />} />
              <Route path="automations" element={<Automations />} />
              <Route path="flow" element={<Flow />} />
              <Route path="blocks" element={<Blocks />} />
              <Route path="models" element={<Models />} />
              <Route path="proposals" element={<Proposals />} />
              <Route path="users" element={<Users />} />
              <Route path="plugins" element={<Plugins />} />
              <Route path="logs" element={<Logs />} />
              <Route path="status" element={<SystemStatus />} />
              <Route path="zigbee" element={<ZigbeeDevices />} />
              <Route path="*" element={<NotFound />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </Suspense>
      <NotificationContainer />
    </CssVarsProvider>
  );
}

export default App;
