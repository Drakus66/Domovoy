import { ThemeProvider } from '@mui/material/styles';
import CssBaseline from '@mui/material/CssBaseline';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import theme from './theme';
import Layout from './components/layout/Layout';
import Dashboard from './pages/Dashboard';
import Logs from './pages/Logs';
import SystemStatus from './pages/SystemStatus';
import ZigbeeDevices from './pages/ZigbeeDevices';
import { NotificationContainer } from './components/common';

function App() {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <BrowserRouter>
        <Routes>
          <Route path="/" element={<Layout />}>
            <Route index element={<Dashboard />} />
            <Route path="logs" element={<Logs />} />
            <Route path="status" element={<SystemStatus />} />
            <Route path="zigbee" element={<ZigbeeDevices />} />
          </Route>
        </Routes>
      </BrowserRouter>
      <NotificationContainer />
    </ThemeProvider>
  );
}

export default App;
