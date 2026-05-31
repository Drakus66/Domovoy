import { Experimental_CssVarsProvider as CssVarsProvider } from '@mui/material/styles';
import CssBaseline from '@mui/material/CssBaseline';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import theme from './theme';
import Layout from './components/layout/Layout';
import Devices from './pages/Devices';
import Zones from './pages/Zones';
import Automations from './pages/Automations';
import Logs from './pages/Logs';
import SystemStatus from './pages/SystemStatus';
import ZigbeeDevices from './pages/ZigbeeDevices';
import { NotificationContainer } from './components/common';

function App() {
  return (
    <CssVarsProvider theme={theme} defaultMode="dark" modeStorageKey="domovoy-color-mode">
      <CssBaseline enableColorScheme />
      <BrowserRouter>
        <Routes>
          <Route path="/" element={<Layout />}>
            <Route index element={<Devices />} />
            <Route path="zones" element={<Zones />} />
            <Route path="automations" element={<Automations />} />
            <Route path="logs" element={<Logs />} />
            <Route path="status" element={<SystemStatus />} />
            <Route path="zigbee" element={<ZigbeeDevices />} />
          </Route>
        </Routes>
      </BrowserRouter>
      <NotificationContainer />
    </CssVarsProvider>
  );
}

export default App;
