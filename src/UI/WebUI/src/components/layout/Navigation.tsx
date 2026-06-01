import { useState } from 'react';
import {
  AppBar, Toolbar, Typography, Box, IconButton, Drawer, List, ListItem,
  ListItemButton, ListItemIcon, ListItemText, useMediaQuery, useTheme,
} from '@mui/material';
import { Link, useLocation } from 'react-router-dom';
import MenuIcon from '@mui/icons-material/Menu';
import SpaceDashboardRoundedIcon from '@mui/icons-material/SpaceDashboardRounded';
import ArticleRoundedIcon from '@mui/icons-material/ArticleRounded';
import MonitorHeartRoundedIcon from '@mui/icons-material/MonitorHeartRounded';
import BluetoothSearchingRoundedIcon from '@mui/icons-material/BluetoothSearchingRounded';
import RoomRoundedIcon from '@mui/icons-material/RoomRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import HomeWorkRoundedIcon from '@mui/icons-material/HomeWorkRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import ExtensionRoundedIcon from '@mui/icons-material/ExtensionRounded';
import ColorModeToggle from '../theme/ColorModeToggle';

const DRAWER_WIDTH = 248;

const navItems = [
  { label: 'Dashboard', path: '/', icon: <SpaceDashboardRoundedIcon /> },
  { label: 'Zones', path: '/zones', icon: <RoomRoundedIcon /> },
  { label: 'Modes', path: '/modes', icon: <HomeWorkRoundedIcon /> },
  { label: 'Automations', path: '/automations', icon: <BoltRoundedIcon /> },
  { label: 'Control blocks', path: '/blocks', icon: <AccountTreeRoundedIcon /> },
  { label: 'Plugins', path: '/plugins', icon: <ExtensionRoundedIcon /> },
  { label: 'Zigbee', path: '/zigbee', icon: <BluetoothSearchingRoundedIcon /> },
  { label: 'System Status', path: '/status', icon: <MonitorHeartRoundedIcon /> },
  { label: 'Logs', path: '/logs', icon: <ArticleRoundedIcon /> },
];

function Brand() {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}>
      <Box
        sx={{
          width: 30, height: 30, borderRadius: 2,
          background: 'linear-gradient(135deg, var(--mui-palette-primary-main), var(--mui-palette-secondary-main))',
          flexShrink: 0,
        }}
      />
      <Typography variant="h6" fontWeight={800} letterSpacing="-0.02em">Domovoy</Typography>
    </Box>
  );
}

function NavList({ onNavigate }: { onNavigate?: () => void }) {
  const location = useLocation();
  return (
    <List sx={{ px: 1.5, py: 1, flex: 1 }}>
      {navItems.map((item) => {
        const selected = location.pathname === item.path;
        return (
          <ListItem key={item.path} disablePadding sx={{ mb: 0.5 }}>
            <ListItemButton
              component={Link}
              to={item.path}
              onClick={onNavigate}
              selected={selected}
              sx={{
                borderRadius: 2,
                minHeight: 46,
                '&.Mui-selected': {
                  bgcolor: 'action.selected',
                  color: 'primary.main',
                  '& .MuiListItemIcon-root': { color: 'primary.main' },
                  '&:hover': { bgcolor: 'action.selected' },
                },
              }}
            >
              <ListItemIcon sx={{ minWidth: 38, color: 'text.secondary' }}>{item.icon}</ListItemIcon>
              <ListItemText primary={item.label} primaryTypographyProps={{ fontWeight: selected ? 700 : 500 }} />
            </ListItemButton>
          </ListItem>
        );
      })}
    </List>
  );
}

function SidebarContent({ onNavigate }: { onNavigate?: () => void }) {
  return (
    <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
      <Box sx={{ px: 2.5, py: 2.5 }}><Brand /></Box>
      <NavList onNavigate={onNavigate} />
      <Box sx={{ px: 2.5, py: 2, display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="caption" color="text.secondary">v1.0</Typography>
        <ColorModeToggle />
      </Box>
    </Box>
  );
}

function Navigation() {
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('md'));
  const [mobileOpen, setMobileOpen] = useState(false);

  if (isMobile) {
    return (
      <>
        <AppBar position="fixed">
          <Toolbar>
            <IconButton aria-label="open drawer" edge="start" onClick={() => setMobileOpen(true)} sx={{ mr: 1 }}>
              <MenuIcon />
            </IconButton>
            <Box sx={{ flexGrow: 1 }}><Brand /></Box>
            <ColorModeToggle />
          </Toolbar>
        </AppBar>
        <Drawer
          variant="temporary"
          open={mobileOpen}
          onClose={() => setMobileOpen(false)}
          ModalProps={{ keepMounted: true }}
          sx={{ '& .MuiDrawer-paper': { width: DRAWER_WIDTH, boxSizing: 'border-box' } }}
        >
          <SidebarContent onNavigate={() => setMobileOpen(false)} />
        </Drawer>
      </>
    );
  }

  return (
    <Box
      component="nav"
      sx={{
        width: DRAWER_WIDTH,
        flexShrink: 0,
        height: '100vh',
        position: 'sticky',
        top: 0,
        borderRight: '1px solid',
        borderColor: 'divider',
        bgcolor: 'background.paper',
      }}
    >
      <SidebarContent />
    </Box>
  );
}

export default Navigation;
