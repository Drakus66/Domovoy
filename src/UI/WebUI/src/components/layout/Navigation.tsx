// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useState } from 'react';
import {
  AppBar, Toolbar, Typography, Box, IconButton, Drawer, List, ListItem,
  ListItemButton, ListItemIcon, ListItemText, Collapse, useMediaQuery, useTheme,
} from '@mui/material';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'react-router-dom';
import MenuIcon from '@mui/icons-material/Menu';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import SpaceDashboardRoundedIcon from '@mui/icons-material/SpaceDashboardRounded';
import ArticleRoundedIcon from '@mui/icons-material/ArticleRounded';
import MonitorHeartRoundedIcon from '@mui/icons-material/MonitorHeartRounded';
import BluetoothSearchingRoundedIcon from '@mui/icons-material/BluetoothSearchingRounded';
import DevicesRoundedIcon from '@mui/icons-material/DevicesRounded';
import RoomRoundedIcon from '@mui/icons-material/RoomRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import MovieFilterRoundedIcon from '@mui/icons-material/MovieFilterRounded';
import HomeWorkRoundedIcon from '@mui/icons-material/HomeWorkRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import ExtensionRoundedIcon from '@mui/icons-material/ExtensionRounded';
import PsychologyRoundedIcon from '@mui/icons-material/PsychologyRounded';
import RuleRoundedIcon from '@mui/icons-material/RuleRounded';
import GroupRoundedIcon from '@mui/icons-material/GroupRounded';
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded';
import ColorModeToggle from '../theme/ColorModeToggle';
import ThemePicker from '../theme/ThemePicker';
import LanguagePicker from '../i18n/LanguagePicker';
import AccountMenu from '../auth/AccountMenu';
import BrandHearth from './BrandHearth';

const DRAWER_WIDTH = 248;

// `key` maps to a nav.json translation; the path/icon stay code-side. Items are grouped
// into collapsible sections (`groups.*` in nav.json) so the sidebar stays short as the
// menu grows — only the section holding the active route is expanded by default.
const navGroups = [
  {
    key: 'home',
    items: [
      { key: 'dashboard', path: '/', icon: <SpaceDashboardRoundedIcon /> },
      { key: 'zones', path: '/zones', icon: <RoomRoundedIcon /> },
      { key: 'modes', path: '/modes', icon: <HomeWorkRoundedIcon /> },
    ],
  },
  {
    key: 'automation',
    items: [
      { key: 'automations', path: '/automations', icon: <BoltRoundedIcon /> },
      { key: 'scenes', path: '/scenes', icon: <MovieFilterRoundedIcon /> },
      { key: 'blocks', path: '/blocks', icon: <AccountTreeRoundedIcon /> },
      { key: 'models', path: '/models', icon: <PsychologyRoundedIcon /> },
      { key: 'proposals', path: '/proposals', icon: <RuleRoundedIcon /> },
    ],
  },
  {
    key: 'devices',
    items: [
      { key: 'registry', path: '/devices', icon: <DevicesRoundedIcon /> },
      { key: 'plugins', path: '/plugins', icon: <ExtensionRoundedIcon /> },
      { key: 'zigbee', path: '/zigbee', icon: <BluetoothSearchingRoundedIcon /> },
    ],
  },
  {
    key: 'system',
    items: [
      { key: 'users', path: '/users', icon: <GroupRoundedIcon /> },
      { key: 'status', path: '/status', icon: <MonitorHeartRoundedIcon /> },
      { key: 'logs', path: '/logs', icon: <ArticleRoundedIcon /> },
      { key: 'settings', path: '/settings', icon: <SettingsRoundedIcon /> },
    ],
  },
];

function Brand() {
  // One mark instead of the old gradient square + ember pair: the avatar is the status now.
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}>
      <BrandHearth />
      <Typography variant="h6" fontWeight={800} letterSpacing="-0.02em">Domovoy</Typography>
    </Box>
  );
}

function NavList({ onNavigate }: { onNavigate?: () => void }) {
  const location = useLocation();
  const { t } = useTranslation('nav');
  const activeGroup = navGroups.find((g) => g.items.some((i) => i.path === location.pathname))?.key;
  // Start with the active section open; the user can toggle any section freely afterwards.
  const [open, setOpen] = useState<Record<string, boolean>>(
    () => Object.fromEntries(navGroups.map((g) => [g.key, g.key === activeGroup])),
  );
  const toggle = (key: string) => setOpen((prev) => ({ ...prev, [key]: !prev[key] }));

  return (
    <List sx={{ px: 1.5, py: 1, flex: 1, overflowY: 'auto' }}>
      {navGroups.map((group) => {
        const expanded = open[group.key] ?? false;
        return (
          <Box key={group.key} sx={{ mb: 0.5 }}>
            <ListItemButton onClick={() => toggle(group.key)} sx={{ borderRadius: 2, py: 0.5, minHeight: 36 }}>
              <ListItemText
                primary={t(`groups.${group.key}`)}
                primaryTypographyProps={{
                  variant: 'overline', fontWeight: 700, color: 'text.secondary', letterSpacing: '0.08em', lineHeight: 1.6,
                }}
              />
              <ExpandMoreRoundedIcon
                fontSize="small"
                sx={{ color: 'text.secondary', transition: 'transform 0.2s', transform: expanded ? 'rotate(180deg)' : 'none' }}
              />
            </ListItemButton>
            <Collapse in={expanded} timeout="auto">
              <List disablePadding>
                {group.items.map((item) => {
                  const selected = location.pathname === item.path;
                  return (
                    <ListItem key={item.path} disablePadding sx={{ mb: 0.25 }}>
                      <ListItemButton
                        component={Link}
                        to={item.path}
                        onClick={onNavigate}
                        selected={selected}
                        sx={{
                          borderRadius: 2,
                          minHeight: 42,
                          pl: 2,
                          '&.Mui-selected': {
                            bgcolor: 'action.selected',
                            color: 'primary.main',
                            '& .MuiListItemIcon-root': { color: 'primary.main' },
                            '&:hover': { bgcolor: 'action.selected' },
                          },
                        }}
                      >
                        <ListItemIcon sx={{ minWidth: 34, color: 'text.secondary' }}>{item.icon}</ListItemIcon>
                        <ListItemText
                          primary={t(item.key)}
                          primaryTypographyProps={{ fontWeight: selected ? 700 : 500, noWrap: true }}
                        />
                      </ListItemButton>
                    </ListItem>
                  );
                })}
              </List>
            </Collapse>
          </Box>
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
        <Box sx={{ display: 'flex', alignItems: 'center' }}>
          <LanguagePicker />
          <ThemePicker />
          <ColorModeToggle />
          <AccountMenu />
        </Box>
      </Box>
    </Box>
  );
}

function Navigation() {
  const theme = useTheme();
  const { t } = useTranslation('common');
  const isMobile = useMediaQuery(theme.breakpoints.down('md'));
  const [mobileOpen, setMobileOpen] = useState(false);

  if (isMobile) {
    return (
      <>
        <AppBar position="fixed">
          <Toolbar>
            <IconButton aria-label={t('actions.openMenu')} edge="start" onClick={() => setMobileOpen(true)} sx={{ mr: 1 }}>
              <MenuIcon />
            </IconButton>
            <Box sx={{ flexGrow: 1 }}><Brand /></Box>
            <LanguagePicker />
            <ThemePicker />
            <ColorModeToggle />
            <AccountMenu />
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
