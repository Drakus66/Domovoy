// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState, useCallback } from 'react';
import {
  Container,
  Typography,
  Box,
  Grid,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Alert,
  IconButton,
  Tooltip,
  Divider,
  LinearProgress,
} from '@mui/material';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import { fmtTime } from '../i18n/format';
import RefreshIcon from '@mui/icons-material/Refresh';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import ErrorIcon from '@mui/icons-material/Error';
import MemoryIcon from '@mui/icons-material/Memory';
import RouterIcon from '@mui/icons-material/Router';
import StorageIcon from '@mui/icons-material/Storage';
import WifiIcon from '@mui/icons-material/Wifi';
import SpeedIcon from '@mui/icons-material/Speed';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import { metricsApi, ServiceStatus, SystemSummary } from '../api/metrics';
import { assistantApi, AssistantStatus } from '../api/assistant';
import SystemControlCard from '../components/system/SystemControlCard';

const REFRESH_INTERVAL_MS = 30_000;

const SERVICE_LABELS: Record<string, { label: string; icon: JSX.Element }> = {
  'api-gateway':            { label: 'API Gateway',             icon: <RouterIcon fontSize="small" /> },
  'db-gateway':             { label: 'DB Gateway',              icon: <StorageIcon fontSize="small" /> },
  'unified-device-service': { label: 'Device Service',          icon: <WifiIcon fontSize="small" /> },
  'connectivity-service':   { label: 'Connectivity Service',    icon: <WifiIcon fontSize="small" /> },
  'rabbitmq':               { label: 'RabbitMQ',                icon: <RouterIcon fontSize="small" /> },
  'mongodb-exporter':       { label: 'MongoDB',                 icon: <StorageIcon fontSize="small" /> },
  'prometheus':             { label: 'Prometheus',              icon: <SpeedIcon fontSize="small" /> },
};

function SummaryCard({ title, value, unit, icon, color = 'text.primary' }: {
  title: string;
  value: number | string | null;
  unit?: string;
  icon: JSX.Element;
  color?: string;
}) {
  return (
    <Card variant="outlined" sx={{ height: '100%' }}>
      <CardContent>
        <Box display="flex" alignItems="center" gap={1} mb={1}>
          <Box sx={{ color: 'text.secondary' }}>{icon}</Box>
          <Typography variant="body2" color="text.secondary">
            {title}
          </Typography>
        </Box>
        <Typography variant="h4" fontWeight={600} sx={{ color }}>
          {value ?? '—'}
          {value !== null && unit && (
            <Typography component="span" variant="body1" color="text.secondary" ml={0.5}>
              {unit}
            </Typography>
          )}
        </Typography>
      </CardContent>
    </Card>
  );
}

function ServiceRow({ service }: { service: ServiceStatus }) {
  const meta = SERVICE_LABELS[service.name];
  return (
    <Box
      display="flex"
      alignItems="center"
      justifyContent="space-between"
      py={1}
      px={2}
      sx={{ '&:not(:last-child)': { borderBottom: '1px solid', borderColor: 'divider' } }}
    >
      <Box display="flex" alignItems="center" gap={1.5}>
        <Box sx={{ color: service.isUp ? 'success.main' : 'error.main', display: 'flex' }}>
          {service.isUp ? <CheckCircleIcon fontSize="small" /> : <ErrorIcon fontSize="small" />}
        </Box>
        <Box sx={{ color: 'text.secondary', display: 'flex' }}>
          {meta?.icon}
        </Box>
        <Box>
          <Typography variant="body2" fontWeight={500}>
            {meta?.label ?? service.name}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {service.instance}
          </Typography>
        </Box>
      </Box>
      <Chip
        label={service.isUp ? i18n.t('status:status.online') : i18n.t('status:status.offline')}
        size="small"
        color={service.isUp ? 'success' : 'error'}
        variant="outlined"
      />
    </Box>
  );
}

function SystemStatus() {
  const { t } = useTranslation('status');
  const [services, setServices] = useState<ServiceStatus[]>([]);
  const [summary, setSummary] = useState<SystemSummary | null>(null);
  const [assistant, setAssistant] = useState<AssistantStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  const fetchData = useCallback(async () => {
    setError(null);
    try {
      const [svc, sum] = await Promise.all([
        metricsApi.getServicesStatus(),
        metricsApi.getSummary(),
      ]);
      setServices(svc);
      setSummary(sum);
      setLastUpdated(new Date());
      // Assistant status is optional (Epic 2H, feature-flagged stub) — never fail the page over it.
      assistantApi.getStatus().then(setAssistant).catch(() => setAssistant(null));
    } catch {
      setError(t('loadError'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, REFRESH_INTERVAL_MS);
    return () => clearInterval(interval);
  }, [fetchData]);

  const servicesOnline = services.filter((s) => s.isUp).length;
  const allOnline = services.length > 0 && servicesOnline === services.length;
  const healthPct = services.length > 0 ? Math.round((servicesOnline / services.length) * 100) : 0;

  return (
    <Container maxWidth="lg">
      <Box sx={{ py: 4 }}>

        {/* Header */}
        <Box display="flex" alignItems="center" justifyContent="space-between" mb={3}>
          <Box>
            <Typography variant="h4" fontWeight={600}>
              {t('title')}
            </Typography>
            {lastUpdated && (
              <Typography variant="caption" color="text.secondary">
                {t('updatedAt', { time: fmtTime(lastUpdated) })}
              </Typography>
            )}
          </Box>
          <Tooltip title={t('refreshNow')}>
            <span>
              <IconButton onClick={fetchData} disabled={loading}>
                <RefreshIcon />
              </IconButton>
            </span>
          </Tooltip>
        </Box>

        {loading && <LinearProgress sx={{ mb: 3, borderRadius: 1 }} />}

        {error && (
          <Alert severity="warning" sx={{ mb: 3 }} onClose={() => setError(null)}>
            {error}
          </Alert>
        )}

        {/* Summary cards */}
        <Grid container spacing={2} mb={3}>
          <Grid item xs={6} sm={3}>
            <SummaryCard
              title={t('cards.servicesOnline')}
              value={services.length ? `${servicesOnline} / ${services.length}` : null}
              icon={<CheckCircleIcon />}
              color={allOnline ? 'success.main' : 'warning.main'}
            />
          </Grid>
          <Grid item xs={6} sm={3}>
            <SummaryCard
              title={t('cards.mqttConnections')}
              value={summary?.mqttConnections ?? null}
              icon={<WifiIcon />}
            />
          </Grid>
          <Grid item xs={6} sm={3}>
            <SummaryCard
              title={t('cards.memoryUsage')}
              value={summary?.memoryMb ?? null}
              unit="MB"
              icon={<MemoryIcon />}
            />
          </Grid>
          <Grid item xs={6} sm={3}>
            <SummaryCard
              title={t('cards.apiRate')}
              value={summary?.apiRequestRate !== null && summary?.apiRequestRate !== undefined
                ? summary.apiRequestRate.toFixed(2)
                : null}
              unit="rps"
              icon={<SpeedIcon />}
            />
          </Grid>
        </Grid>

        {/* Health bar */}
        {services.length > 0 && (
          <Box mb={3}>
            <Box display="flex" justifyContent="space-between" mb={0.5}>
              <Typography variant="body2" color="text.secondary">
                {t('overallHealth')}
              </Typography>
              <Typography variant="body2" fontWeight={600}>
                {healthPct}%
              </Typography>
            </Box>
            <LinearProgress
              variant="determinate"
              value={healthPct}
              color={healthPct === 100 ? 'success' : healthPct >= 50 ? 'warning' : 'error'}
              sx={{ height: 8, borderRadius: 4 }}
            />
          </Box>
        )}

        {/* Natural-language assistant (Epic 2H) — a feature-flagged extension point, disabled by default. */}
        {assistant && (
          <Card variant="outlined" sx={{ mb: 3 }}>
            <CardContent sx={{ display: 'flex', alignItems: 'center', gap: 2, '&:last-child': { pb: 2 } }}>
              <Box sx={{ color: 'text.secondary', display: 'flex' }}><AutoAwesomeRoundedIcon /></Box>
              <Box flex={1} minWidth={0}>
                <Typography variant="subtitle1" fontWeight={600}>{t('assistant.title')}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {assistant.available
                    ? t('assistant.enabled', { provider: assistant.provider })
                    : t('assistant.disabled')}
                </Typography>
              </Box>
              <Chip
                size="small"
                variant="outlined"
                color={assistant.available ? 'success' : 'default'}
                label={assistant.available ? i18n.t('status:status.online') : t('assistant.stub')}
              />
            </CardContent>
          </Card>
        )}

        {/* System control (restart services / containers) */}
        <SystemControlCard />

        {/* Services list */}
        <Card variant="outlined">
          <Box px={2} pt={2} pb={1}>
            <Typography variant="subtitle1" fontWeight={600}>
              {t('services')}
            </Typography>
          </Box>
          <Divider />
          {loading && services.length === 0 ? (
            <Box display="flex" justifyContent="center" py={4}>
              <CircularProgress size={32} />
            </Box>
          ) : services.length === 0 ? (
            <Box py={4} textAlign="center">
              <Typography color="text.secondary">{t('noData')}</Typography>
            </Box>
          ) : (
            services
              .slice()
              .sort((a, b) => (a.isUp === b.isUp ? a.name.localeCompare(b.name) : a.isUp ? -1 : 1))
              .map((s) => <ServiceRow key={`${s.name}-${s.instance}`} service={s} />)
          )}
        </Card>

      </Box>
    </Container>
  );
}

export default SystemStatus;
