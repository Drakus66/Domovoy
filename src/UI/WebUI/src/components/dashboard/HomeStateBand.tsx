// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { ReactNode, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  Box, Card, CardActionArea, CardContent, Grid, LinearProgress, Stack, Typography, useTheme,
} from '@mui/material';
import type { SvgIconComponent } from '@mui/icons-material';
import HomeWorkRoundedIcon from '@mui/icons-material/HomeWorkRounded';
import ThermostatRoundedIcon from '@mui/icons-material/ThermostatRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import type { AggregateBucket } from '../../api/history';
import { homeMode } from '../../store/liveData';
import { CATEGORY_ACCENT } from '../devices/deviceVisuals';
import { deviceLabel } from '../devices/deviceNaming';
import { fmtTime } from '../../i18n/format';
import { useTelemetryBatch } from '../charts/useTelemetryBatch';
import Sparkline from '../charts/Sparkline';
import DomovoyDigest from '../common/DomovoyDigest';
import HearthMark from '../common/HearthMark';
import FlipCard from '../common/FlipCard';
import ModesTile from './items/ModesTile';
import {
  summarizeHome, pickClimateTrendDevice, pickEnergyTrendDevice, openSecurityItems, topPowerConsumers,
} from './homeSummary';

// Matches the DeviceTile sparkline options so every band + tile series shares ONE batched request.
const TREND_OPTS = { hours: 24, bucket: 'hour', maxPoints: 24 } as const;
// ~12 points on the face — a hint of shape, not a chart.
const TREND_POINTS = 12;

/**
 * "State of the home" hero band above the device list (roadmap dashboard fill, block A). Gives the house
 * a pulse — mode, climate, security, energy — plus the house-spirit's one-line digest, before the grid of
 * appliances. Cards are shortcuts into the matching sphere / the modes page; each card flips to a detail
 * side (the flip trigger is separate, so the click-through navigation is untouched).
 */
export default function HomeStateBand({ devices }: { devices: CapabilityDevice[] }) {
  const { t } = useTranslation('dashboards');
  const { t: tm } = useTranslation('modes');
  const navigate = useNavigate();
  // Из общего слоя: этот же режим независимо запрашивал вложенный сюда DomovoyDigest.
  const mode = homeMode.use().data ?? null;

  const summary = useMemo(() => summarizeHome(devices), [devices]);
  const climateDevice = useMemo(() => pickClimateTrendDevice(devices), [devices]);
  const energyDevice = useMemo(() => pickEnergyTrendDevice(devices), [devices]);
  const security = useMemo(() => openSecurityItems(devices), [devices]);
  // Three rows fit the fixed band-card height; anything longer would scroll inside the flip side.
  const consumers = useMemo(() => topPowerConsumers(devices, 3), [devices]);

  const climateTrend = useTelemetryBatch(climateDevice?.id, 'temperature', {
    ...TREND_OPTS, enabled: !!climateDevice,
  });
  const energyTrend = useTelemetryBatch(energyDevice?.id, 'power', {
    ...TREND_OPTS, enabled: !!energyDevice,
  });

  const climateSub = [
    summary.climate.humidity !== undefined ? t('band.humidity', { value: summary.climate.humidity }) : null,
    summary.climate.co2 !== undefined ? t('band.co2', { value: summary.climate.co2 }) : null,
  ].filter(Boolean).join(' · ');

  const secure = summary.security.openCount === 0;
  const securityValue = summary.security.locks === 0 && summary.security.openCount === 0
    ? t('band.empty')
    : secure ? t('band.secure') : t('band.open', { count: summary.security.openCount });
  const securitySub = summary.security.locks === 0
    ? t('band.noLocks')
    : secure ? t('band.allSecure') : '';

  const climateRange = useMemo(() => {
    const buckets = (climateTrend.buckets ?? []).slice(-TREND_POINTS);
    if (buckets.length < 2) return null;
    return {
      min: Math.min(...buckets.map((b) => b.min)),
      max: Math.max(...buckets.map((b) => b.max)),
    };
  }, [climateTrend.buckets]);

  const maxConsumerWatts = Math.max(1, ...consumers.map((c) => c.watts));

  return (
    <Box sx={{ mb: 3 }}>
      {/* Voice of the house spirit (reused digest) marked by the hearth sign. */}
      <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 2 }}>
        <HearthMark size={18} />
        <DomovoyDigest />
      </Stack>

      <Grid container spacing={1.75}>
        <StatCard
          icon={HomeWorkRoundedIcon} accent="secondary.main" label={t('band.mode')}
          value={mode ? tm(`names.${mode.mode}`, { defaultValue: mode.mode }) : t('band.empty')}
          sub={mode ? t('band.since', { time: fmtTime(mode.updatedAt, { hour: '2-digit', minute: '2-digit' }) }) : ''}
          onClick={() => navigate('/modes')}
          // The whole modes switcher as the flip side: more useful than a mode history, zero new
          // code. The wrapper stretches its `height: 100%` card to the flip footprint and lets
          // taller content grow into FlipCard's scroll instead of clipping.
          back={(
            <Box
              sx={{
                minHeight: '100%', display: 'flex', flexDirection: 'column',
                '& > .MuiCard-root': { flexGrow: 1, height: 'auto' },
              }}
            >
              <ModesTile />
            </Box>
          )}
        />
        <StatCard
          icon={ThermostatRoundedIcon} accent={CATEGORY_ACCENT.climate} label={t('band.climate')}
          value={summary.climate.temp !== undefined ? `${summary.climate.temp}°` : t('band.empty')}
          sub={climateSub}
          trend={climateTrend.buckets}
          onClick={() => navigate('/t/sphere:climate')}
          back={(
            <BackCard icon={ThermostatRoundedIcon} accent={CATEGORY_ACCENT.climate} label={t('band.climate')}>
              <Stack spacing={0.5}>
                {summary.climate.humidity !== undefined && (
                  <Typography variant="body2">{t('band.humidity', { value: summary.climate.humidity })}</Typography>
                )}
                {summary.climate.co2 !== undefined && (
                  <Typography variant="body2">{t('band.co2', { value: summary.climate.co2 })}</Typography>
                )}
                {climateRange && (
                  <Typography variant="body2">
                    {t('band.back.range12h', { min: `${climateRange.min}°`, max: `${climateRange.max}°` })}
                  </Typography>
                )}
                {climateDevice && (
                  <Typography variant="caption" color="text.secondary">
                    {t('band.back.source', { name: deviceLabel(climateDevice) })}
                  </Typography>
                )}
              </Stack>
            </BackCard>
          )}
        />
        <StatCard
          icon={LockRoundedIcon}
          accent={secure ? 'success.main' : 'warning.main'}
          valueColor={summary.security.locks > 0 ? (secure ? 'success.main' : 'warning.main') : undefined}
          label={t('band.security')} value={securityValue} sub={securitySub}
          onClick={() => navigate('/t/sphere:security')}
          back={(
            <BackCard
              icon={LockRoundedIcon}
              accent={secure ? 'success.main' : 'warning.main'}
              label={t('band.security')}
            >
              <Stack spacing={0.5}>
                {security.length === 0 ? (
                  <Typography variant="body2">{t('band.back.allClosed')}</Typography>
                ) : (
                  security.slice(0, 4).map(({ device, kind }) => (
                    <Stack key={`${device.id}-${kind}`} direction="row" spacing={1} alignItems="baseline">
                      <Typography variant="body2" noWrap flex={1}>{deviceLabel(device)}</Typography>
                      <Typography variant="caption" color="warning.main">
                        {kind === 'lock' ? t('band.back.unlocked') : t('band.back.open')}
                      </Typography>
                    </Stack>
                  ))
                )}
                <Typography variant="caption" color="text.secondary">
                  {t('band.back.locksTotal', { count: summary.security.locks })}
                </Typography>
              </Stack>
            </BackCard>
          )}
        />
        <StatCard
          icon={BoltRoundedIcon} accent={CATEGORY_ACCENT.energy} label={t('band.energy')}
          value={t('band.watts', { value: summary.energy.watts })}
          sub={summary.energy.kwh > 0 ? t('band.kwh', { value: summary.energy.kwh }) : ''}
          trend={energyTrend.buckets}
          onClick={() => navigate('/t/sphere:energy')}
          back={(
            <BackCard icon={BoltRoundedIcon} accent={CATEGORY_ACCENT.energy} label={t('band.energy')}>
              <Stack spacing={0.75}>
                {consumers.length > 0 && (
                  <Typography variant="caption" color="text.secondary">
                    {t('band.back.topConsumers')}
                  </Typography>
                )}
                {consumers.map(({ device, watts }) => (
                  <Box key={device.id}>
                    <Stack direction="row" spacing={1} alignItems="baseline">
                      <Typography variant="body2" noWrap flex={1}>{deviceLabel(device)}</Typography>
                      <Typography variant="caption" color="text.secondary">
                        {t('band.watts', { value: Math.round(watts) })}
                      </Typography>
                    </Stack>
                    <LinearProgress
                      variant="determinate"
                      value={Math.min(100, (watts / maxConsumerWatts) * 100)}
                      sx={{
                        height: 4, borderRadius: 2, mt: 0.25,
                        bgcolor: 'action.hover',
                        '& .MuiLinearProgress-bar': { bgcolor: CATEGORY_ACCENT.energy },
                      }}
                    />
                  </Box>
                ))}
                {summary.energy.kwh > 0 && (
                  <Typography variant="caption" color="text.secondary">
                    {t('band.back.meterTotal', { value: summary.energy.kwh })}
                  </Typography>
                )}
              </Stack>
            </BackCard>
          )}
        />
      </Grid>
    </Box>
  );
}

/** The flip side of a band card: same icon + overline header, dense detail rows below. */
function BackCard({
  icon: Icon, accent, label, children,
}: { icon: SvgIconComponent; accent: string; label: string; children: ReactNode }) {
  return (
    // minHeight (not height): overflow must reach FlipCard's scroll container, not clip in the Card.
    <Card variant="outlined" sx={{ minHeight: '100%' }}>
      <CardContent sx={{ p: 1.75, '&:last-child': { pb: 1.75 } }}>
        <Stack direction="row" alignItems="center" spacing={0.75} sx={{ mb: 1, pr: 4 }}>
          <Icon sx={{ fontSize: 16, color: accent }} />
          <Typography variant="overline" sx={{ color: 'text.secondary', letterSpacing: '.06em', lineHeight: 1 }}>
            {label}
          </Typography>
        </Stack>
        {children}
      </CardContent>
    </Card>
  );
}

function StatCard({
  icon: Icon, accent, label, value, sub, valueColor, onClick, trend, back,
}: {
  icon: SvgIconComponent;
  accent: string;
  label: string;
  value: string;
  sub: string;
  valueColor?: string;
  onClick: () => void;
  trend?: AggregateBucket[] | null;
  back?: ReactNode;
}) {
  const theme = useTheme();
  const { t } = useTranslation('common');

  const face = (
    // The face defines the flip footprint; the min height gives typical backs room to fit
    // without scrolling (the band row itself never resizes on flip).
    <Card variant="outlined" sx={{ height: '100%', minHeight: 150 }}>
      {/* ButtonBase is a centering flexbox — pin the content to the top like a plain card. */}
      <CardActionArea
        onClick={onClick}
        sx={{
          height: '100%', p: 1.75,
          display: 'flex', flexDirection: 'column', alignItems: 'stretch', justifyContent: 'flex-start',
        }}
      >
        {/* pr clears the flip trigger sitting in the tile's top-right corner. */}
        <Stack direction="row" alignItems="center" spacing={0.75} sx={{ mb: 1, pr: back ? 4 : 0 }}>
          <Icon sx={{ fontSize: 16, color: accent }} />
          <Typography
            variant="overline"
            sx={{ color: 'text.secondary', letterSpacing: '.06em', lineHeight: 1 }}
          >
            {label}
          </Typography>
        </Stack>
        <Typography sx={{ fontSize: '1.5rem', fontWeight: 700, lineHeight: 1, color: valueColor }}>
          {value}
        </Typography>
        {sub && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
            {sub}
          </Typography>
        )}
        {trend && trend.length >= 2 && (
          <Box sx={{ mt: 0.75 }}>
            <Sparkline buckets={trend.slice(-TREND_POINTS)} color={theme.palette.text.disabled} height={20} />
          </Box>
        )}
      </CardActionArea>
    </Card>
  );

  return (
    <Grid item xs={12} sm={6} md={3}>
      {back ? (
        <FlipCard
          front={face}
          back={back}
          flipLabel={t('flip.details')}
          backLabel={t('flip.back')}
          // Lazy: the mode switcher back fetches on mount, and until the first flip the DOM
          // stays byte-identical to a plain card.
          lazyBack
        />
      ) : face}
    </Grid>
  );
}
