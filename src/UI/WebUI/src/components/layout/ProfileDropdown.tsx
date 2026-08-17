// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import {
  Avatar, Box, ButtonBase, Divider, IconButton, ListItemIcon, ListItemText,
  Menu, MenuItem, Stack, Tooltip, Typography,
} from '@mui/material';
import { useColorScheme } from '@mui/material/styles';
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded';
import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded';
import PaletteRoundedIcon from '@mui/icons-material/PaletteRounded';
import TranslateRoundedIcon from '@mui/icons-material/TranslateRounded';
import ChevronRightRoundedIcon from '@mui/icons-material/ChevronRightRounded';
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded';
import LockResetRoundedIcon from '@mui/icons-material/LockResetRounded';
import LogoutRoundedIcon from '@mui/icons-material/LogoutRounded';
import UnfoldMoreRoundedIcon from '@mui/icons-material/UnfoldMoreRounded';
import { useAuthStore } from '../../store/authStore';
import { ThemeMenu } from '../theme/ThemePicker';
import { LanguageMenu } from '../i18n/LanguagePicker';
import ChangePasswordDialog from '../auth/ChangePasswordDialog';
import HearthMark from '../common/HearthMark';

const VERSION_LABEL = 'Domovoy v1.0';

const initialsOf = (name: string) =>
  name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') || '?';

/**
 * The profile block replacing the row of four sidebar icons: one avatar trigger, one menu —
 * light/dark, theme and language submenus, settings, and (when someone is actually signed in)
 * password change + sign out. Works with auth off too: the account section simply drops out and
 * the hearth mark stands in for the avatar (a LAN-only home has a house spirit, not an account).
 */
export default function ProfileDropdown({ variant }: { variant: 'sidebar' | 'bar' }) {
  const { t } = useTranslation(['common', 'auth']);
  const user = useAuthStore((s) => s.user);
  const logout = useAuthStore((s) => s.logout);
  const { mode, systemMode, setMode } = useColorScheme();
  const resolvedMode = (mode === 'system' ? systemMode : mode) ?? 'dark';

  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [themeAnchor, setThemeAnchor] = useState<HTMLElement | null>(null);
  const [langAnchor, setLangAnchor] = useState<HTMLElement | null>(null);
  const [pwOpen, setPwOpen] = useState(false);

  // Auth off → the gateway hands back the synthetic admin; there's no account to manage.
  const realUser = user && user.id !== 'dev-admin' ? user : null;
  const displayName = realUser ? (realUser.displayName || realUser.username) : t('common:profile.local');

  const closeAll = () => {
    setThemeAnchor(null);
    setLangAnchor(null);
    setAnchor(null);
  };

  const avatar = realUser ? (
    <Avatar sx={{ width: variant === 'sidebar' ? 32 : 28, height: variant === 'sidebar' ? 32 : 28, fontSize: '0.8rem', fontWeight: 700 }}>
      {initialsOf(displayName)}
    </Avatar>
  ) : (
    <Avatar sx={{ width: variant === 'sidebar' ? 32 : 28, height: variant === 'sidebar' ? 32 : 28, bgcolor: 'action.selected' }}>
      <HearthMark size={18} />
    </Avatar>
  );

  const trigger = variant === 'sidebar' ? (
    <ButtonBase
      onClick={(e) => setAnchor(e.currentTarget)}
      aria-label={t('common:profile.menu')}
      aria-haspopup="menu"
      sx={{
        width: '100%', justifyContent: 'flex-start', textAlign: 'left',
        px: 1.25, py: 1, borderRadius: 2,
        '&:hover': { bgcolor: 'action.hover' },
      }}
    >
      <Stack direction="row" alignItems="center" spacing={1.25} sx={{ width: '100%', minWidth: 0 }}>
        {avatar}
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography variant="body2" fontWeight={700} noWrap>{displayName}</Typography>
          <Typography variant="caption" color="text.secondary" noWrap display="block">
            {realUser ? realUser.username : VERSION_LABEL}
          </Typography>
        </Box>
        <UnfoldMoreRoundedIcon fontSize="small" sx={{ color: 'text.secondary' }} />
      </Stack>
    </ButtonBase>
  ) : (
    <Tooltip title={t('common:profile.menu')}>
      <IconButton
        onClick={(e) => setAnchor(e.currentTarget)}
        aria-label={t('common:profile.menu')}
        aria-haspopup="menu"
        size="small"
      >
        {avatar}
      </IconButton>
    </Tooltip>
  );

  const submenuOrigins = {
    anchorOrigin: { vertical: 'top', horizontal: 'right' },
    transformOrigin: { vertical: 'top', horizontal: 'left' },
  } as const;

  return (
    <>
      {trigger}
      <Menu
        anchorEl={anchor}
        open={Boolean(anchor)}
        onClose={closeAll}
        anchorOrigin={variant === 'sidebar'
          ? { vertical: 'top', horizontal: 'center' }
          : { vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={variant === 'sidebar'
          ? { vertical: 'bottom', horizontal: 'center' }
          : { vertical: 'top', horizontal: 'right' }}
        slotProps={{ paper: { sx: { minWidth: 264 } } }}
      >
        <Box sx={{ px: 2, py: 1 }}>
          <Typography variant="caption" color="text.secondary">
            {realUser ? t('auth:account.signedInAs') : t('common:profile.localHint')}
          </Typography>
          <Typography variant="body2" fontWeight={700} noWrap>{displayName}</Typography>
        </Box>
        <Divider />
        <MenuItem
          onClick={() => setMode?.(resolvedMode === 'dark' ? 'light' : 'dark')}
          // Deliberately keeps the menu open — flipping the mode previews instantly.
        >
          <ListItemIcon>
            {resolvedMode === 'dark'
              ? <LightModeRoundedIcon fontSize="small" />
              : <DarkModeRoundedIcon fontSize="small" />}
          </ListItemIcon>
          <ListItemText>
            {resolvedMode === 'dark' ? t('common:colorMode.toLight') : t('common:colorMode.toDark')}
          </ListItemText>
        </MenuItem>
        <MenuItem onClick={(e) => setThemeAnchor(e.currentTarget)} aria-haspopup="menu">
          <ListItemIcon><PaletteRoundedIcon fontSize="small" /></ListItemIcon>
          <ListItemText>{t('common:theme.label')}</ListItemText>
          <ChevronRightRoundedIcon fontSize="small" sx={{ color: 'text.secondary' }} />
        </MenuItem>
        <MenuItem onClick={(e) => setLangAnchor(e.currentTarget)} aria-haspopup="menu">
          <ListItemIcon><TranslateRoundedIcon fontSize="small" /></ListItemIcon>
          <ListItemText>{t('common:language.label')}</ListItemText>
          <ChevronRightRoundedIcon fontSize="small" sx={{ color: 'text.secondary' }} />
        </MenuItem>
        <Divider />
        <MenuItem component={Link} to="/settings" onClick={closeAll}>
          <ListItemIcon><SettingsRoundedIcon fontSize="small" /></ListItemIcon>
          <ListItemText>{t('common:profile.settings')}</ListItemText>
        </MenuItem>
        {realUser && <Divider />}
        {realUser && (
          <MenuItem onClick={() => { closeAll(); setPwOpen(true); }}>
            <ListItemIcon><LockResetRoundedIcon fontSize="small" /></ListItemIcon>
            <ListItemText>{t('auth:account.changePassword')}</ListItemText>
          </MenuItem>
        )}
        {realUser && (
          <MenuItem onClick={() => { closeAll(); void logout(); }}>
            <ListItemIcon><LogoutRoundedIcon fontSize="small" /></ListItemIcon>
            <ListItemText>{t('auth:account.logout')}</ListItemText>
          </MenuItem>
        )}
        <Divider />
        <Box sx={{ px: 2, py: 0.75 }}>
          <Typography variant="caption" color="text.secondary">{VERSION_LABEL}</Typography>
        </Box>
      </Menu>

      <ThemeMenu
        anchorEl={themeAnchor}
        open={Boolean(themeAnchor)}
        onClose={() => setThemeAnchor(null)}
        onSelected={closeAll}
        {...submenuOrigins}
      />
      <LanguageMenu
        anchorEl={langAnchor}
        open={Boolean(langAnchor)}
        onClose={() => setLangAnchor(null)}
        onSelected={closeAll}
        {...submenuOrigins}
      />
      <ChangePasswordDialog open={pwOpen} onClose={() => setPwOpen(false)} />
    </>
  );
}
