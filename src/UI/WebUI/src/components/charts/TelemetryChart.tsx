// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useTranslation } from 'react-i18next';
import { Box, Typography, Skeleton } from '@mui/material';
import {
  ResponsiveContainer, AreaChart, Area, XAxis, YAxis, Tooltip, CartesianGrid,
} from 'recharts';
import { useAggregateSeries } from './useAggregateSeries';
import { fmtTime } from '../../i18n/format';
import {
  useChartPalette, useChartGradientId, gridProps, xAxisProps, yAxisProps, cursorProps,
} from './chartKit';
import { AreaGradientDefs, ChartCrosshairTooltip } from './chartChrome';

interface Props {
  capabilityId: string;
  deviceId?: string;
  zoneId?: string;
  unit?: string | null;
  /** Look-back window in hours (default 24). */
  hours?: number;
  bucket?: 'minute' | 'hour' | 'day';
  height?: number;
}

/**
 * Telemetry trend chart (roadmap Epic 1B). Renders a min/avg/max band over time from the on-the-fly
 * aggregation endpoint (e.g. "zone temperature over the last 24h"). Scoped to a device or a zone.
 */
export default function TelemetryChart({
  capabilityId, deviceId, zoneId, unit, hours = 24, bucket = 'hour', height = 200,
}: Props) {
  const palette = useChartPalette();
  // Per-instance gradient id: two charts of the same capability on one page must not share <defs>.
  const gradientId = useChartGradientId();
  const { t } = useTranslation('devices');
  // Device series coalesce through the shared batch loader; zone series fall back to a direct fetch.
  const { buckets: data, error } = useAggregateSeries({ capabilityId, deviceId, zoneId, hours, bucket });

  if (error) return <Typography variant="caption" color="text.secondary">{t('chart.loadError')}</Typography>;
  if (data === null) return <Skeleton variant="rounded" height={height} />;
  if (data.length === 0) {
    return <Typography variant="caption" color="text.secondary">{t('chart.noSamples', { hours })}</Typography>;
  }

  const series = data.map((b) => ({
    t: new Date(b.timestamp).getTime(),
    avg: Number(b.avg.toFixed(2)),
    min: Number(b.min.toFixed(2)),
    max: Number(b.max.toFixed(2)),
  }));

  const fmtAxisTick = (ts: number) =>
    fmtTime(ts, bucket === 'day' ? { month: 'short', day: 'numeric' } : { hour: '2-digit', minute: '2-digit' });

  return (
    <Box sx={{ width: '100%', height }}>
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={series} margin={{ top: 4, right: 8, bottom: 0, left: -16 }}>
          <AreaGradientDefs id={gradientId} color={palette.series} />
          <CartesianGrid {...gridProps(palette)} />
          <XAxis
            dataKey="t" type="number" scale="time" domain={['dataMin', 'dataMax']}
            tickFormatter={fmtAxisTick} minTickGap={32}
            {...xAxisProps(palette)}
          />
          <YAxis
            width={44}
            tickFormatter={(v) => `${v}${unit ?? ''}`}
            domain={['auto', 'auto']}
            {...yAxisProps(palette)}
          />
          <Tooltip content={<ChartCrosshairTooltip unit={unit} />} cursor={cursorProps(palette)} />
          <Area
            type="monotone" dataKey="avg" name={t('chart.avg')}
            stroke={palette.series} strokeWidth={2} strokeLinecap="round" strokeLinejoin="round"
            fill={`url(#${gradientId})`}
            isAnimationActive={false} dot={false}
          />
        </AreaChart>
      </ResponsiveContainer>
    </Box>
  );
}
