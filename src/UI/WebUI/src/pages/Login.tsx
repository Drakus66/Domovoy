// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { FormEvent, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AxiosError } from 'axios';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { useAuthStore } from '../store/authStore';

/**
 * Full-screen login (mobile-app / remote-access track). Rendered by the top-level gate when the gateway reports
 * auth is required and we're not signed in. On success the store flips to `authenticated` and the routed app
 * mounts — there is no separate route, so a deep link is preserved (the URL never changed).
 */
export default function Login() {
  const { t } = useTranslation('auth');
  const login = useAuthStore((s) => s.login);

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(username, password);
    } catch (err) {
      setError(messageFor(err, t));
      setSubmitting(false);
    }
  };

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        p: 2,
        bgcolor: 'background.default',
      }}
    >
      <Card sx={{ width: '100%', maxWidth: 400 }} elevation={4}>
        <CardContent sx={{ p: 4 }}>
          <Stack spacing={1} sx={{ mb: 3 }}>
            <Typography variant="h5" component="h1" fontWeight={700}>
              {t('login.title')}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {t('login.subtitle')}
            </Typography>
          </Stack>

          <form onSubmit={onSubmit}>
            <Stack spacing={2}>
              {error && <Alert severity="error">{error}</Alert>}
              <TextField
                label={t('login.username')}
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoComplete="username"
                autoFocus
                fullWidth
                required
                disabled={submitting}
              />
              <TextField
                label={t('login.password')}
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                fullWidth
                required
                disabled={submitting}
              />
              <Button
                type="submit"
                variant="contained"
                size="large"
                fullWidth
                disabled={submitting || !username || !password}
                startIcon={submitting ? <CircularProgress size={18} color="inherit" /> : undefined}
              >
                {submitting ? t('login.signingIn') : t('login.submit')}
              </Button>
            </Stack>
          </form>
        </CardContent>
      </Card>
    </Box>
  );
}

function messageFor(err: unknown, t: (k: string) => string): string {
  if (err instanceof AxiosError) {
    const status = err.response?.status;
    if (status === 401) return t('login.invalidCredentials');
    if (status === 429) return t('login.tooManyAttempts');
    if (status === 503) return t('login.backendUnavailable');
  }
  return t('login.genericError');
}
