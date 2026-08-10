// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert, Box, Button, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle,
  Divider, FormControlLabel, LinearProgress, Radio, RadioGroup, Stack, Switch, Table, TableBody,
  TableCell, TableHead, TableRow, Typography,
} from '@mui/material';
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded';
import SystemUpdateAltRoundedIcon from '@mui/icons-material/SystemUpdateAltRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import {
  ComponentStatus, UpdatePlan, UpdateRun, UpdateSettings, updatesApi,
} from '../../api/updates';
import { confirmAction } from '../../store/confirmStore';

/** A run is in flight — the poller keeps going and the buttons stay locked. */
const isRunning = (run: UpdateRun | { status: string } | null): run is UpdateRun =>
  run !== null && run.status === 'running';

/**
 * Updates section of the settings page (roadmap Epic 3K).
 *
 * Three things the owner needs and nothing else: which channel the house follows, what is installed
 * versus available per component, and — before anything happens — exactly what pressing "update"
 * would do, including whatever it drags in and why.
 *
 * Progress is polled rather than pushed on purpose: applying an update recreates the api-gateway and
 * this very UI near the end, so the connection is expected to drop. The run state lives in a file on
 * the host, so the page picks the story back up when it reconnects.
 */
export default function UpdatesEditor() {
  const { t } = useTranslation('updates');

  const [settings, setSettings] = useState<UpdateSettings | null>(null);
  const [components, setComponents] = useState<ComponentStatus[] | null>(null);
  const [channel, setChannel] = useState<string>('release');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [plan, setPlan] = useState<UpdatePlan | null>(null);
  const [planTarget, setPlanTarget] = useState<string[] | null>(null);
  const [run, setRun] = useState<UpdateRun | null>(null);
  const [history, setHistory] = useState<UpdateRun[]>([]);

  const pollRef = useRef<number | null>(null);

  const loadComponents = useCallback(() => {
    updatesApi.components()
      .then((r) => { setComponents(r.components); setChannel(r.channel); })
      // Служба обновлений опциональна: стек без неё должен показывать понятную пустоту, а не ошибку.
      .catch(() => setComponents(null));
  }, []);

  const loadStatus = useCallback(() => {
    updatesApi.status()
      .then((s) => setRun(isRunning(s) || s.status !== 'idle' ? (s as UpdateRun) : null))
      .catch(() => undefined);
  }, []);

  useEffect(() => {
    updatesApi.getSettings().then((s) => { setSettings(s); setChannel(s.channel); }).catch(() => setSettings(null));
    loadComponents();
    loadStatus();
    updatesApi.history().then(setHistory).catch(() => setHistory([]));
  }, [loadComponents, loadStatus]);

  // Опрос только пока идёт прогон: в покое страница настроек не должна фонить запросами.
  useEffect(() => {
    if (!isRunning(run)) {
      if (pollRef.current) { window.clearInterval(pollRef.current); pollRef.current = null; }
      return;
    }
    if (pollRef.current) return;

    pollRef.current = window.setInterval(loadStatus, 2000);
    return () => {
      if (pollRef.current) { window.clearInterval(pollRef.current); pollRef.current = null; }
    };
  }, [run, loadStatus]);

  const save = async (patch: Partial<UpdateSettings>) => {
    setError(null);
    try {
      const saved = await updatesApi.saveSettings(patch);
      setSettings(saved);
      if (patch.channel) {
        setChannel(patch.channel);
        setNotice(t('channelSwitched', { channel: t(`channels.${patch.channel}`) }));
        loadComponents();
      }
    } catch {
      setError(t('errors.save'));
    }
  };

  const checkNow = async () => {
    setBusy(true);
    setError(null);
    try {
      await updatesApi.check();
      loadComponents();
      setNotice(t('checked'));
    } catch {
      setError(t('errors.check'));
    } finally {
      setBusy(false);
    }
  };

  const openPlan = async (target: string[] | null) => {
    setBusy(true);
    setError(null);
    setPlanTarget(target);
    try {
      setPlan(await updatesApi.plan(target));
    } catch {
      setError(t('errors.plan'));
    } finally {
      setBusy(false);
    }
  };

  const applyPlan = async () => {
    setBusy(true);
    try {
      await updatesApi.apply(planTarget, settings?.backupBeforeUpdate ?? true);
      setPlan(null);
      setNotice(t('started'));
      window.setTimeout(loadStatus, 1000);
    } catch {
      setError(t('errors.apply'));
    } finally {
      setBusy(false);
    }
  };

  const rollback = async () => {
    if (!await confirmAction({ message: t('rollbackConfirm') })) return;
    setBusy(true);
    try {
      await updatesApi.rollback();
      setNotice(t('rollbackStarted'));
      window.setTimeout(loadStatus, 1000);
    } catch {
      setError(t('errors.rollback'));
    } finally {
      setBusy(false);
    }
  };

  const outdated = (components ?? []).filter((c) => c.hasUpdate);

  if (components === null && settings === null) {
    return <Alert severity="info">{t('unavailable')}</Alert>;
  }

  return (
    <Box>
      {error && <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
      {notice && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setNotice(null)}>{notice}</Alert>}

      {/* ---- канал ---- */}
      <Typography variant="body2" fontWeight={600} mb={1}>{t('channel')}</Typography>
      <RadioGroup
        row
        value={channel}
        onChange={(e) => save({ channel: e.target.value as 'release' | 'dev' })}
      >
        <FormControlLabel value="release" control={<Radio size="small" />} label={t('channels.release')} />
        <FormControlLabel value="dev" control={<Radio size="small" />} label={t('channels.dev')} />
      </RadioGroup>
      <Typography variant="caption" color="text.secondary" display="block" mb={2}>
        {t(channel === 'dev' ? 'channelHint.dev' : 'channelHint.release')}
      </Typography>

      <Stack direction="row" spacing={2} alignItems="center" mb={2} flexWrap="wrap" useFlexGap>
        <FormControlLabel
          control={(
            <Switch
              size="small"
              checked={settings?.checkEnabled ?? true}
              onChange={(e) => save({ checkEnabled: e.target.checked })}
            />
          )}
          label={t('checkEnabled')}
        />
        <FormControlLabel
          control={(
            <Switch
              size="small"
              checked={settings?.backupBeforeUpdate ?? true}
              onChange={(e) => save({ backupBeforeUpdate: e.target.checked })}
            />
          )}
          label={t('backupBeforeUpdate')}
        />
      </Stack>

      <Divider sx={{ my: 2 }} />

      {/* ---- прогресс идущего обновления ---- */}
      {run && (
        <Alert
          severity={run.status === 'error' ? 'error' : run.status === 'running' ? 'info' : 'success'}
          sx={{ mb: 2 }}
        >
          <Typography variant="body2" fontWeight={600}>{t(`runStatus.${run.status}`, run.status)}</Typography>
          {run.status === 'running' && (
            <>
              <LinearProgress sx={{ my: 1 }} />
              <Typography variant="caption" display="block">{t('uiWillRestart')}</Typography>
            </>
          )}
          {run.steps.map((s) => (
            <Typography key={s.name} variant="caption" display="block">
              {`${s.status === 'ok' ? '✓' : s.status === 'error' ? '✕' : '·'} ${s.name}`}
              {s.detail ? ` — ${s.detail}` : ''}
            </Typography>
          ))}
          {run.envKeysAdded.length > 0 && (
            <Typography variant="caption" display="block" mt={1}>
              {t('envKeysAdded', { keys: run.envKeysAdded.join(', ') })}
            </Typography>
          )}
          {run.error && <Typography variant="caption" display="block" mt={1}>{run.error}</Typography>}
        </Alert>
      )}

      {/* ---- компоненты ---- */}
      <Box display="flex" alignItems="center" justifyContent="space-between" mb={1} gap={1} flexWrap="wrap">
        <Typography variant="body2" fontWeight={600}>{t('components')}</Typography>
        <Stack direction="row" spacing={1}>
          <Button
            size="small"
            startIcon={busy ? <CircularProgress size={14} /> : <RefreshRoundedIcon />}
            disabled={busy || isRunning(run)}
            onClick={checkNow}
          >
            {t('checkNow')}
          </Button>
          <Button
            size="small"
            variant="contained"
            startIcon={<SystemUpdateAltRoundedIcon />}
            disabled={busy || isRunning(run) || outdated.length === 0}
            onClick={() => openPlan(null)}
          >
            {t('updateAll')}
          </Button>
        </Stack>
      </Box>

      <Box sx={{ overflowX: 'auto' }}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t('table.component')}</TableCell>
              <TableCell>{t('table.installed')}</TableCell>
              <TableCell>{t('table.available')}</TableCell>
              <TableCell align="right" />
            </TableRow>
          </TableHead>
          <TableBody>
            {(components ?? []).map((c) => (
              <TableRow key={c.name}>
                <TableCell>{c.name}</TableCell>
                <TableCell>
                  <Typography variant="caption">{c.installed ?? '—'}</Typography>
                </TableCell>
                <TableCell>
                  {c.hasUpdate
                    ? <Chip size="small" color="primary" variant="outlined" label={c.available} />
                    : <Typography variant="caption" color="text.secondary">{t('upToDate')}</Typography>}
                </TableCell>
                <TableCell align="right">
                  <Button
                    size="small"
                    disabled={busy || isRunning(run) || !c.hasUpdate}
                    onClick={() => openPlan([c.name])}
                  >
                    {t('update')}
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Box>

      {/* ---- история ---- */}
      {history.length > 0 && (
        <>
          <Divider sx={{ my: 2 }} />
          <Box display="flex" alignItems="center" justifyContent="space-between" mb={1}>
            <Typography variant="body2" fontWeight={600}>
              <HistoryRoundedIcon fontSize="inherit" sx={{ mr: 0.5, verticalAlign: 'middle' }} />
              {t('history')}
            </Typography>
            <Button size="small" color="warning" disabled={busy || isRunning(run)} onClick={rollback}>
              {t('rollback')}
            </Button>
          </Box>
          {history.slice(0, 5).map((h) => (
            <Typography key={h.id} variant="caption" display="block" color="text.secondary">
              {new Date(h.startedAt).toLocaleString()} — {t(`runStatus.${h.status}`, h.status)}
              {h.plan.length > 0 ? ` (${h.plan.map((p) => p.component).join(', ')})` : ''}
            </Typography>
          ))}
        </>
      )}

      {/* ---- диалог: что именно произойдёт ---- */}
      <Dialog open={plan !== null} onClose={() => setPlan(null)} maxWidth="sm" fullWidth>
        <DialogTitle>{t('planTitle')}</DialogTitle>
        <DialogContent>
          {plan && !plan.ok && (
            <Alert severity="error">
              <Typography variant="body2">{plan.refusal}</Typography>
              {plan.conflict.length > 0 && (
                <Typography variant="caption" display="block" mt={1}>
                  {t('conflict', { components: plan.conflict.join(' ↔ ') })}
                </Typography>
              )}
            </Alert>
          )}

          {plan?.ok && plan.empty && <Alert severity="info">{t('nothingToDo')}</Alert>}

          {plan?.ok && !plan.empty && plan.groups.map((g, gi) => (
            <Box key={gi} mb={2}>
              {g.atomic && (
                <Alert severity="warning" sx={{ mb: 1 }}>
                  <Typography variant="caption">{t('atomicGroup')}</Typography>
                </Alert>
              )}
              {g.members.map((m) => (
                <Box key={m.component} mb={1}>
                  <Typography variant="body2">
                    <strong>{m.component}</strong> {m.from} → {m.to}
                  </Typography>
                  <Typography variant="caption" color="text.secondary">{m.reason}</Typography>
                </Box>
              ))}
            </Box>
          ))}

          {plan?.ok && !plan.empty && (
            <Alert severity="info" sx={{ mt: 1 }}>
              <Typography variant="caption">
                {settings?.backupBeforeUpdate ? t('willBackup') : t('willNotBackup')}
              </Typography>
            </Alert>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setPlan(null)}>{t('cancel')}</Button>
          <Button
            variant="contained"
            disabled={busy || !plan?.ok || plan?.empty}
            onClick={applyPlan}
          >
            {t('applyNow')}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
