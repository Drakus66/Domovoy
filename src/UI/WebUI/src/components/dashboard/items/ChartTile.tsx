// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useTranslation } from 'react-i18next';
import { Box, Card, CardContent, Stack, Typography } from '@mui/material';
import type { Capability, CapabilityDevice } from '../../../api/capabilityDevices';
import { capabilityIcon, capabilityLabel } from '../../devices/deviceVisuals';
import { deviceLabel } from '../../devices/deviceNaming';
import { fmtDateTime } from '../../../i18n/format';
import TelemetryChart from '../../charts/TelemetryChart';
import { useAggregateSeries } from '../../charts/useAggregateSeries';
import FlipCard from '../../common/FlipCard';

/**
 * A telemetry-trend widget for one numeric capability of a device (reuses the Epic 1B chart).
 * Window/bucket come from the item's params (editor presets: 6h/minute, 24h/hour, week/day).
 * The front is the chart; the flip side summarises the same series (min / max / mean / last).
 */
export default function ChartTile({
  device, cap, hours, bucket,
}: {
  device: CapabilityDevice;
  cap: Capability;
  hours: number;
  bucket: 'minute' | 'hour' | 'day';
}) {
  const { t } = useTranslation('dashboards');
  const Icon = capabilityIcon(cap.id);
  // Same params as the TelemetryChart below → the same batch key, so the flip side costs no
  // extra round-trip: both read one cached series.
  const { buckets } = useAggregateSeries({ capabilityId: cap.id, deviceId: device.id, hours, bucket });

  const unit = cap.unit ?? '';
  const fmt = (v: number) => `${Number(v.toFixed(2))}${unit}`;

  const stats = (() => {
    if (!buckets || buckets.length === 0) return null;
    const totalCount = buckets.reduce((s, b) => s + b.count, 0);
    const weighted = totalCount > 0
      ? buckets.reduce((s, b) => s + b.avg * b.count, 0) / totalCount
      : buckets.reduce((s, b) => s + b.avg, 0) / buckets.length;
    return {
      min: Math.min(...buckets.map((b) => b.min)),
      max: Math.max(...buckets.map((b) => b.max)),
      avg: weighted,
      last: buckets[buckets.length - 1].avg,
      from: buckets[0].timestamp,
      to: buckets[buckets.length - 1].timestamp,
      samples: totalCount,
    };
  })();

  const header = (
    // pr clears the flip trigger in the tile's top-right corner.
    <Stack direction="row" alignItems="center" spacing={1} mb={1} minWidth={0} pr={4}>
      <Box sx={{ color: 'text.secondary', display: 'flex' }}><Icon fontSize="small" /></Box>
      <Typography variant="subtitle2" fontWeight={700} noWrap>
        {deviceLabel(device)} · {capabilityLabel(cap.id)}
      </Typography>
    </Stack>
  );

  const front = (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        {header}
        <TelemetryChart
          capabilityId={cap.id}
          deviceId={device.id}
          unit={cap.unit}
          hours={hours}
          bucket={bucket}
          height={180}
        />
      </CardContent>
    </Card>
  );

  const back = (
    // minHeight (not height): overflow must reach FlipCard's scroll container, not clip in the Card.
    <Card sx={{ minHeight: '100%' }}>
      <CardContent>
        {header}
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.5, mt: 0.5 }}>
          <BackStat label={t('chartBack.min')} value={stats ? fmt(stats.min) : '—'} />
          <BackStat label={t('chartBack.max')} value={stats ? fmt(stats.max) : '—'} />
          <BackStat label={t('chartBack.avg')} value={stats ? fmt(stats.avg) : '—'} />
          <BackStat label={t('chartBack.last')} value={stats ? fmt(stats.last) : '—'} />
        </Box>
        {stats && (
          <Typography variant="caption" color="text.secondary" display="block" mt={1.5}>
            {t('chartBack.period', { from: fmtDateTime(stats.from), to: fmtDateTime(stats.to) })}
            {' · '}
            {t('chartBack.samples', { count: stats.samples })}
          </Typography>
        )}
      </CardContent>
    </Card>
  );

  return (
    <FlipCard
      front={front}
      back={back}
      flipLabel={t('flip.stats')}
      backLabel={t('common:flip.back')}
    />
  );
}

function BackStat({ label, value }: { label: string; value: string }) {
  return (
    <Box>
      <Typography variant="overline" sx={{ color: 'text.secondary', letterSpacing: '.06em', lineHeight: 1.4, display: 'block' }}>
        {label}
      </Typography>
      <Typography variant="body1" sx={{ fontWeight: 600 }}>{value}</Typography>
    </Box>
  );
}
