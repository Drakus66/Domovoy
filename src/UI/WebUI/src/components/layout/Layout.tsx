import { Box } from '@mui/material';
import { Outlet } from 'react-router-dom';
import Navigation from './Navigation';
import ErrorBoundary from '../common/ErrorBoundary';

function Layout() {
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
      <Navigation />
      <ErrorBoundary>
        <Box component="main" sx={{ flexGrow: 1, bgcolor: 'background.default' }}>
          <Outlet />
        </Box>
      </ErrorBoundary>
    </Box>
  );
}

export default Layout;
