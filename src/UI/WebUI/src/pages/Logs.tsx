// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { fmtDateTime } from '../i18n/format';
import {
  Container, Box, Typography, Stack, Chip, TextField, MenuItem, InputAdornment,
  LinearProgress, Alert, Card, IconButton, Tooltip, Divider, Button,
  ToggleButtonGroup, ToggleButton,
} from '@mui/material';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded';
import DevicesRoundedIcon from '@mui/icons-material/DevicesRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import TerminalRoundedIcon from '@mui/icons-material/TerminalRounded';
import NotificationsRoundedIcon from '@mui/icons-material/NotificationsRounded';
import SendRoundedIcon from '@mui/icons-material/SendRounded';
import { activityApi, ActivityEntry, ActivitySource, ActivitySeverity } from '../api/activity';
import { capabilityDevicesApi } from '../api/capabilityDevices';
import { notificationsApi, NotificationChannels } from '../api/notifications';
import DiaryView from '../components/logs/DiaryView';
import TriggerChip from '../components/common/TriggerChip';

const SOURCE_META: Record<ActivitySource, { icon: JSX.Element; color: 'primary' | 'secondary' | 'default' }> = {
  device: { icon: <DevicesRoundedIcon fontSize="small" />, color: 'primary' },
  automation: { icon: <BoltRoundedIcon fontSize="small" />, color: 'secondary' },
  block: { icon: <AccountTreeRoundedIcon fontSize="small" />, color: 'secondary' },
  system: { icon: <TerminalRoundedIcon fontSize="small" />, color: 'default' },
};

const SEVERITY_COLOR: Record<ActivitySeverity, 'default' | 'warning' | 'error'> = {
  info: 'default', warn: 'warning', error: 'error',
};

const SEVERITY_BAR: Record<ActivitySeverity, string> = {
  info: 'var(--mui-palette-divider)', warn: 'var(--mui-palette-warning-main)', error: 'var(--mui-palette-error-main)',
};

const WINDOWS = [
  { key: 'lastHour', hours: 1 },
  { key: 'last24h', hours: 24 },
  { key: 'last7d', hours: 168 },
];

export default function Logs() {
  const { t } = useTranslation('logs');
  const [entries, setEntries] = useState<ActivityEntry[]>([]);
  const [deviceNames, setDeviceNames] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [source, setSource] = useState<ActivitySource | 'all'>('all');
  const [severity, setSeverity] = useState<ActivitySeverity | 'all'>('all');
  const [hours, setHours] = useState(24);
  const [search, setSearch] = useState('');
  const [channels, setChannels] = useState<NotificationChannels | null>(null);
  const [testMsg, setTestMsg] = useState<string | null>(null);
  const [view, setView] = useState<'activity' | 'diary'>('activity');

  const since = useMemo(() => new Date(Date.now() - hours * 3600 * 1000).toISOString(), [hours]);

  const load = useCallback(async () => {
    setError(null);
    try {
      const rows = await activityApi.get({
        from: since, limit: 500,
        source: source === 'all' ? undefined : source,
        severity: severity === 'all' ? undefined : severity,
        q: search.trim() || undefined,
      });
      setEntries(rows);
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, [since, source, severity, search]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    const t = setInterval(load, 10000);
    return () => clearInterval(t);
  }, [load]);

  // Resolve device ids → names for readable device rows (best-effort).
  useEffect(() => {
    capabilityDevicesApi.getDevices()
      .then((ds) => setDeviceNames(Object.fromEntries(ds.map((d) => [d.id, d.name]))))
      .catch(() => undefined);
  }, []);

  // Notification delivery channels (Epic 2G) — status + a test button.
  useEffect(() => {
    notificationsApi.getChannels().then(setChannels).catch(() => undefined);
  }, []);

  const sendTest = useCallback(async () => {
    setTestMsg(null);
    try {
      const r = await notificationsApi.sendTest();
      setTestMsg(r.delivered > 0 ? t('channels.testOk', { count: r.delivered }) : t('channels.testNone'));
    } catch {
      setTestMsg(t('channels.testError'));
    }
  }, [t]);

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={2}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t('subtitle')}
            </Typography>
          </Box>
          <Stack direction="row" spacing={1} alignItems="center">
            <ToggleButtonGroup size="small" exclusive value={view} onChange={(_, v) => v && setView(v)}>
              <ToggleButton value="activity">{t('view.activity')}</ToggleButton>
              <ToggleButton value="diary">{t('view.diary')}</ToggleButton>
            </ToggleButtonGroup>
            {view === 'activity' && (
              <Tooltip title={t('refresh')}><IconButton onClick={load} disabled={loading}><RefreshRoundedIcon /></IconButton></Tooltip>
            )}
          </Stack>
        </Stack>

        {view === 'diary' ? <DiaryView /> : (<>
        <Card variant="outlined" sx={{ px: 2, py: 1.25, mb: 2 }}>
          <Stack direction="row" spacing={1.5} alignItems="center" flexWrap="wrap" useFlexGap>
            <NotificationsRoundedIcon fontSize="small" color="action" />
            <Typography variant="body2" fontWeight={600}>{t('channels.title')}</Typography>
            {channels && channels.enabled.length > 0 ? (
              <Stack direction="row" spacing={0.5} alignItems="center" flexWrap="wrap" useFlexGap>
                <Typography variant="caption" color="text.secondary">{t('channels.enabled')}</Typography>
                {channels.enabled.map((c) => <Chip key={c} size="small" label={c} color="primary" variant="outlined" />)}
              </Stack>
            ) : (
              <Typography variant="caption" color="text.secondary">{t('channels.none')}</Typography>
            )}
            <Box flex={1} />
            {testMsg && <Typography variant="caption" color="text.secondary">{testMsg}</Typography>}
            <Button size="small" variant="outlined" startIcon={<SendRoundedIcon />}
              disabled={!channels || channels.enabled.length === 0} onClick={sendTest}>
              {t('channels.test')}
            </Button>
          </Stack>
        </Card>

        <Stack direction="row" spacing={1.5} mb={2} flexWrap="wrap" useFlexGap alignItems="center">
          <TextField
            size="small" placeholder={t('searchPlaceholder')} value={search}
            onChange={(e) => setSearch(e.target.value)} sx={{ minWidth: 220 }}
            InputProps={{ startAdornment: (<InputAdornment position="start"><SearchRoundedIcon fontSize="small" /></InputAdornment>) }}
          />
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            <Chip label={t('sources.all')} variant={source === 'all' ? 'filled' : 'outlined'}
              color={source === 'all' ? 'primary' : 'default'} onClick={() => setSource('all')} />
            {(Object.keys(SOURCE_META) as ActivitySource[]).map((s) => (
              <Chip key={s} icon={SOURCE_META[s].icon} label={t(`sources.${s}`)}
                variant={source === s ? 'filled' : 'outlined'}
                color={source === s ? SOURCE_META[s].color : 'default'} onClick={() => setSource(s)} />
            ))}
          </Stack>
          <TextField select size="small" label={t('severity.label')} value={severity} sx={{ width: 130 }}
            onChange={(e) => setSeverity(e.target.value as ActivitySeverity | 'all')}>
            <MenuItem value="all">{t('severity.all')}</MenuItem>
            <MenuItem value="info">{t('severity.info')}</MenuItem>
            <MenuItem value="warn">{t('severity.warn')}</MenuItem>
            <MenuItem value="error">{t('severity.error')}</MenuItem>
          </TextField>
          <TextField select size="small" label={t('window.label')} value={hours} sx={{ width: 140 }}
            onChange={(e) => setHours(Number(e.target.value))}>
            {WINDOWS.map((w) => <MenuItem key={w.hours} value={w.hours}>{t(`window.${w.key}`)}</MenuItem>)}
          </TextField>
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {entries.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <Typography color="text.secondary">{t('empty')}</Typography>
          </Box>
        ) : (
          <Card variant="outlined" sx={{ maxHeight: '65vh', overflowY: 'auto' }}>
            <Stack divider={<Divider />}>
              {entries.map((e, i) => {
                // With a structured initiator the clickable chip replaces the textual "by …" detail;
                // details that carry more than attribution (trigger summaries, errors) stay visible.
                const detail = e.triggerKind && e.detail?.startsWith('by ') ? null : e.detail;
                return (
                <Stack key={`${e.timestamp}-${i}`} direction="row" spacing={1.5} alignItems="flex-start"
                  sx={{ px: 2, py: 1.25, borderLeft: '3px solid', borderLeftColor: SEVERITY_BAR[e.severity] }}>
                  <Box flex={1} minWidth={0}>
                    <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.25}>
                      <Chip size="small" variant="outlined" color={SOURCE_META[e.source]?.color ?? 'default'}
                        label={SOURCE_META[e.source] ? t(`sources.${e.source}`) : e.source} />
                      {e.severity !== 'info' && (
                        <Chip size="small" color={SEVERITY_COLOR[e.severity]} label={t(`severity.${e.severity}`)} />
                      )}
                      <Typography variant="body2" fontWeight={600} sx={{ wordBreak: 'break-word' }}>{e.title}</Typography>
                      {e.triggerKind && (
                        <TriggerChip kind={e.triggerKind} id={e.triggerId} name={e.triggerName} />
                      )}
                    </Stack>
                    {(detail || e.deviceId || e.service) && (
                      <Typography variant="caption" color="text.secondary">
                        {e.deviceId ? `${deviceNames[e.deviceId] ?? e.deviceId} · ` : ''}
                        {e.service ? `${e.service} · ` : ''}
                        {detail ?? ''}
                      </Typography>
                    )}
                  </Box>
                  <Typography variant="caption" color="text.secondary" sx={{ whiteSpace: 'nowrap', pt: 0.25 }}>
                    {fmtDateTime(e.timestamp)}
                  </Typography>
                </Stack>
                );
              })}
            </Stack>
          </Card>
        )}
        </>)}
      </Box>
    </Container>
  );
}
