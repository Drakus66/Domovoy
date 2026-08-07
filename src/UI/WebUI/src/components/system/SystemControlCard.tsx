// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Card, Box, Typography, Divider, Button, Chip, IconButton, Tooltip, Snackbar, Alert,
} from '@mui/material';
import RestartAltRoundedIcon from '@mui/icons-material/RestartAltRounded';
import PowerSettingsNewRoundedIcon from '@mui/icons-material/PowerSettingsNewRounded';
import { systemApi, SystemServiceInfo, SystemServicesResponse } from '../../api/system';
import { updatesApi } from '../../api/updates';
import { confirmAction } from '../../store/confirmStore';

/**
 * Restart services from the UI. Default is a safe self-restart over the bus (the service stops itself, the
 * container restart policy brings it back). Infra/other containers appear only when the opt-in Docker control
 * is enabled and are restarted via the Docker socket.
 */
export default function SystemControlCard() {
  const { t } = useTranslation('status');
  const [data, setData] = useState<SystemServicesResponse | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [toast, setToast] = useState<{ msg: string; sev: 'success' | 'error' } | null>(null);
  // Версии живут у службы обновлений (Эпик 3K). Подтягиваем мягко: без неё карточка работает
  // ровно как раньше, просто без строки версии.
  const [versions, setVersions] = useState<Record<string, string>>({});

  const load = useCallback(() => {
    systemApi.getServices().then(setData).catch(() => setData(null));
    updatesApi.components()
      .then((r) => setVersions(Object.fromEntries(
        r.components.filter((c) => c.installed).map((c) => [c.name, c.installed as string]),
      )))
      .catch(() => setVersions({}));
  }, []);
  useEffect(() => { load(); }, [load]);

  const act = async (key: string, label: string, fn: () => Promise<void>) => {
    setBusy(key);
    try {
      await fn();
      setToast({ msg: t('control.requested', { name: label }), sev: 'success' });
    } catch {
      setToast({ msg: t('control.failed', { name: label }), sev: 'error' });
    } finally {
      setBusy(null);
      // Give the restart a few seconds, then refresh the (Docker) state.
      window.setTimeout(load, 3000);
    }
  };

  const restartAll = async () => {
    if (!await confirmAction({ message: t('control.confirmAll') })) return;
    act('all', t('control.allServices'), () => systemApi.restartAll());
  };

  const restartOne = async (s: SystemServiceInfo) => {
    if (!await confirmAction({ message: t('control.confirmOne', { name: s.name }) })) return;
    // A .NET service self-restarts over the bus; anything else needs the Docker path.
    act(s.name, s.name, () => systemApi.restartService(s.name, !s.selfRestart));
  };

  const stateColor = (state?: string | null): 'success' | 'error' | 'default' =>
    state === 'running' ? 'success' : state ? 'error' : 'default';

  return (
    <Card variant="outlined" sx={{ mb: 3 }}>
      <Box px={2} pt={2} pb={1} display="flex" alignItems="center" justifyContent="space-between" gap={1}>
        <Box>
          <Typography variant="subtitle1" fontWeight={600}>{t('control.title')}</Typography>
          <Typography variant="caption" color="text.secondary">
            {data?.dockerEnabled ? t('control.dockerOn') : t('control.dockerOff')}
          </Typography>
        </Box>
        <Button
          size="small"
          color="warning"
          variant="outlined"
          startIcon={<RestartAltRoundedIcon />}
          disabled={busy !== null}
          onClick={restartAll}
        >
          {t('control.restartAll')}
        </Button>
      </Box>
      <Divider />

      {(data?.services ?? []).map((s) => (
        <Box
          key={s.name}
          display="flex"
          alignItems="center"
          justifyContent="space-between"
          py={1}
          px={2}
          sx={{ '&:not(:last-child)': { borderBottom: '1px solid', borderColor: 'divider' } }}
        >
          <Box minWidth={0}>
            <Typography variant="body2" fontWeight={500} noWrap>{s.name}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t(`control.kind.${s.kind}`, s.kind)}
              {versions[s.name] ? ` · ${versions[s.name]}` : ''}
            </Typography>
          </Box>
          <Box display="flex" alignItems="center" gap={1}>
            {s.state && (
              <Chip size="small" variant="outlined" color={stateColor(s.state)} label={s.state} />
            )}
            {s.selfRestart
              ? <Chip size="small" variant="outlined" label={t('control.selfRestart')} />
              : <Chip size="small" variant="outlined" color="warning" label={t('control.dockerOnly')} />}
            <Tooltip title={t('control.restart')}>
              <span>
                <IconButton
                  size="small"
                  color="warning"
                  disabled={busy !== null || (!s.selfRestart && !data?.dockerEnabled)}
                  onClick={() => restartOne(s)}
                >
                  {s.selfRestart ? <RestartAltRoundedIcon fontSize="small" /> : <PowerSettingsNewRoundedIcon fontSize="small" />}
                </IconButton>
              </span>
            </Tooltip>
          </Box>
        </Box>
      ))}

      <Snackbar
        open={toast !== null}
        autoHideDuration={4000}
        onClose={() => setToast(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        {toast ? <Alert severity={toast.sev} onClose={() => setToast(null)}>{toast.msg}</Alert> : undefined}
      </Snackbar>
    </Card>
  );
}
