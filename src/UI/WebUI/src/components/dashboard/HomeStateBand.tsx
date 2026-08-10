// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { Box, Card, CardActionArea, Grid, Stack, Typography } from '@mui/material';
import type { SvgIconComponent } from '@mui/icons-material';
import HomeWorkRoundedIcon from '@mui/icons-material/HomeWorkRounded';
import ThermostatRoundedIcon from '@mui/icons-material/ThermostatRounded';
import LockRoundedIcon from '@mui/icons-material/LockRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import { homeMode } from '../../store/liveData';
import { CATEGORY_ACCENT } from '../devices/deviceVisuals';
import { fmtTime } from '../../i18n/format';
import DomovoyDigest from '../common/DomovoyDigest';
import HearthMark from '../common/HearthMark';
import { summarizeHome } from './homeSummary';


/**
 * "State of the home" hero band above the device list (roadmap dashboard fill, block A). Gives the house
 * a pulse — mode, climate, security, energy — plus the house-spirit's one-line digest, before the grid of
 * appliances. Cards are shortcuts into the matching sphere / the modes page.
 */
export default function HomeStateBand({ devices }: { devices: CapabilityDevice[] }) {
  const { t } = useTranslation('dashboards');
  const { t: tm } = useTranslation('modes');
  const navigate = useNavigate();
  // Из общего слоя: этот же режим независимо запрашивал вложенный сюда DomovoyDigest.
  const mode = homeMode.use().data ?? null;

  const summary = useMemo(() => summarizeHome(devices), [devices]);

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

  return (
    <Box sx={{ mb: 3 }}>
      {/* Voice of the house spirit (reused digest) marked by the hearth sign. */}
      <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 2 }}>
        <HearthMark size={18} />
        <DomovoyDigest />
      </Stack>

      <Grid container spacing={1.75}>
        <StatCard
          xs
          icon={HomeWorkRoundedIcon} accent="secondary.main" label={t('band.mode')}
          value={mode ? tm(`names.${mode.mode}`, { defaultValue: mode.mode }) : t('band.empty')}
          sub={mode ? t('band.since', { time: fmtTime(mode.updatedAt, { hour: '2-digit', minute: '2-digit' }) }) : ''}
          onClick={() => navigate('/modes')}
        />
        <StatCard
          xs
          icon={ThermostatRoundedIcon} accent={CATEGORY_ACCENT.climate} label={t('band.climate')}
          value={summary.climate.temp !== undefined ? `${summary.climate.temp}°` : t('band.empty')}
          sub={climateSub}
          onClick={() => navigate('/t/sphere:climate')}
        />
        <StatCard
          xs
          icon={LockRoundedIcon}
          accent={secure ? 'success.main' : 'warning.main'}
          valueColor={summary.security.locks > 0 ? (secure ? 'success.main' : 'warning.main') : undefined}
          label={t('band.security')} value={securityValue} sub={securitySub}
          onClick={() => navigate('/t/sphere:security')}
        />
        <StatCard
          xs
          icon={BoltRoundedIcon} accent={CATEGORY_ACCENT.energy} label={t('band.energy')}
          value={t('band.watts', { value: summary.energy.watts })}
          sub={summary.energy.kwh > 0 ? t('band.kwh', { value: summary.energy.kwh }) : ''}
          onClick={() => navigate('/t/sphere:energy')}
        />
      </Grid>
    </Box>
  );
}

function StatCard({
  icon: Icon, accent, label, value, sub, valueColor, onClick,
}: {
  xs?: boolean;
  icon: SvgIconComponent;
  accent: string;
  label: string;
  value: string;
  sub: string;
  valueColor?: string;
  onClick: () => void;
}) {
  return (
    <Grid item xs={12} sm={6} md={3}>
      <Card variant="outlined" sx={{ height: '100%' }}>
        <CardActionArea onClick={onClick} sx={{ height: '100%', p: 1.75 }}>
          <Stack direction="row" alignItems="center" spacing={0.75} sx={{ mb: 1 }}>
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
        </CardActionArea>
      </Card>
    </Grid>
  );
}
