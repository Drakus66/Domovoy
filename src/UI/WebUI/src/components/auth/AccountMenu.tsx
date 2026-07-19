// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { FormEvent, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AxiosError } from 'axios';
import {
  Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, Divider,
  IconButton, ListItemIcon, ListItemText, Menu, MenuItem, Stack, TextField, Tooltip, Typography,
} from '@mui/material';
import AccountCircleRoundedIcon from '@mui/icons-material/AccountCircleRounded';
import LockResetRoundedIcon from '@mui/icons-material/LockResetRounded';
import LogoutRoundedIcon from '@mui/icons-material/LogoutRounded';
import { useAuthStore } from '../../store/authStore';
import { authApi } from '../../api/auth';

/**
 * Signed-in account control for the sidebar / app bar (mobile-app / remote-access track): shows who is signed in
 * and offers self-service password change + sign out. Hidden entirely when auth is off (the synthetic dev admin),
 * so a LAN-only/dev deployment shows no misleading "sign out".
 */
export default function AccountMenu() {
  const { t } = useTranslation('auth');
  const user = useAuthStore((s) => s.user);
  const logout = useAuthStore((s) => s.logout);

  const [anchor, setAnchor] = useState<null | HTMLElement>(null);
  const [pwOpen, setPwOpen] = useState(false);

  // Auth off → the gateway hands back the synthetic admin; there's nothing to sign out of.
  if (!user || user.id === 'dev-admin') return null;

  return (
    <>
      <Tooltip title={t('account.menu')}>
        <IconButton size="small" onClick={(e) => setAnchor(e.currentTarget)} aria-label={t('account.menu')}>
          <AccountCircleRoundedIcon />
        </IconButton>
      </Tooltip>

      <Menu anchorEl={anchor} open={Boolean(anchor)} onClose={() => setAnchor(null)}>
        <Box sx={{ px: 2, py: 1 }}>
          <Typography variant="caption" color="text.secondary">{t('account.signedInAs')}</Typography>
          <Typography variant="body2" fontWeight={700}>{user.displayName || user.username}</Typography>
        </Box>
        <Divider />
        <MenuItem onClick={() => { setAnchor(null); setPwOpen(true); }}>
          <ListItemIcon><LockResetRoundedIcon fontSize="small" /></ListItemIcon>
          <ListItemText>{t('account.changePassword')}</ListItemText>
        </MenuItem>
        <MenuItem onClick={() => { setAnchor(null); void logout(); }}>
          <ListItemIcon><LogoutRoundedIcon fontSize="small" /></ListItemIcon>
          <ListItemText>{t('account.logout')}</ListItemText>
        </MenuItem>
      </Menu>

      <ChangePasswordDialog open={pwOpen} onClose={() => setPwOpen(false)} />
    </>
  );
}

function ChangePasswordDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation('auth');
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState(false);
  const [busy, setBusy] = useState(false);

  const reset = () => { setCurrent(''); setNext(''); setConfirm(''); setError(null); setOk(false); setBusy(false); };
  const close = () => { reset(); onClose(); };

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (next.length < 8) { setError(t('changePassword.tooShort')); return; }
    if (next !== confirm) { setError(t('changePassword.mismatch')); return; }
    setBusy(true);
    try {
      await authApi.changePassword(current, next);
      setOk(true);
      setBusy(false);
      setTimeout(close, 1200);
    } catch (err) {
      setBusy(false);
      const status = err instanceof AxiosError ? err.response?.status : undefined;
      setError(status === 400 ? t('changePassword.wrongCurrent') : t('changePassword.genericError'));
    }
  };

  return (
    <Dialog open={open} onClose={close} maxWidth="xs" fullWidth>
      <DialogTitle>{t('changePassword.title')}</DialogTitle>
      <form onSubmit={onSubmit}>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 0.5 }}>
            {error && <Alert severity="error">{error}</Alert>}
            {ok && <Alert severity="success">{t('changePassword.success')}</Alert>}
            <TextField
              label={t('changePassword.current')} type="password" value={current}
              onChange={(e) => setCurrent(e.target.value)} autoComplete="current-password" required fullWidth disabled={busy || ok}
            />
            <TextField
              label={t('changePassword.new')} type="password" value={next}
              onChange={(e) => setNext(e.target.value)} autoComplete="new-password" required fullWidth disabled={busy || ok}
            />
            <TextField
              label={t('changePassword.confirm')} type="password" value={confirm}
              onChange={(e) => setConfirm(e.target.value)} autoComplete="new-password" required fullWidth disabled={busy || ok}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={close} disabled={busy}>{t('changePassword.cancel')}</Button>
          <Button type="submit" variant="contained" disabled={busy || ok || !current || !next || !confirm}>
            {t('changePassword.submit')}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
