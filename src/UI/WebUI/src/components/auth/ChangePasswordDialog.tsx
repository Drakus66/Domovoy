// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { FormEvent, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AxiosError } from 'axios';
import {
  Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField,
} from '@mui/material';
import { authApi } from '../../api/auth';

/** Self-service password change (mobile-app / remote-access track). */
export default function ChangePasswordDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
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
