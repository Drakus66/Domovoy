// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Typography, Skeleton, useTheme } from '@mui/material';
import {
  ResponsiveContainer, AreaChart, Area, XAxis, YAxis, Tooltip, CartesianGrid,
} from 'recharts';
import { historyApi, AggregateBucket } from '../../api/history';
import { fmtTime, fmtDateTime } from '../../i18n/format';

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
  const theme = useTheme();
  const { t } = useTranslation('devices');
  const [data, setData] = useState<AggregateBucket[] | null>(null);
  const [error, setError] = useState(false);

  const from = useMemo(() => new Date(Date.now() - hours * 3600 * 1000).toISOString(), [hours]);

  useEffect(() => {
    let cancelled = false;
    setData(null);
    setError(false);
    historyApi
      .getAggregate({ capabilityId, deviceId, zoneId, from, bucket, agg: 'avg' })
      .then((rows) => { if (!cancelled) setData(rows); })
      .catch(() => { if (!cancelled) setError(true); });
    return () => { cancelled = true; };
  }, [capabilityId, deviceId, zoneId, from, bucket]);

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

  const accent = theme.palette.primary.main;

  return (
    <Box sx={{ width: '100%', height }}>
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={series} margin={{ top: 4, right: 8, bottom: 0, left: -16 }}>
          <defs>
            <linearGradient id={`grad-${capabilityId}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={accent} stopOpacity={0.35} />
              <stop offset="100%" stopColor={accent} stopOpacity={0.02} />
            </linearGradient>
          </defs>
          <CartesianGrid strokeDasharray="3 3" stroke={theme.palette.divider} vertical={false} />
          <XAxis
            dataKey="t" type="number" scale="time" domain={['dataMin', 'dataMax']}
            tickFormatter={fmtAxisTick} tick={{ fontSize: 11, fill: theme.palette.text.secondary }}
            minTickGap={32} stroke={theme.palette.divider}
          />
          <YAxis
            width={44} tick={{ fontSize: 11, fill: theme.palette.text.secondary }}
            stroke={theme.palette.divider}
            tickFormatter={(v) => `${v}${unit ?? ''}`}
            domain={['auto', 'auto']}
          />
          <Tooltip
            contentStyle={{
              background: theme.palette.background.paper,
              border: `1px solid ${theme.palette.divider}`,
              borderRadius: 8, fontSize: 12,
            }}
            labelFormatter={(label) => fmtDateTime(new Date(Number(label)).toISOString())}
            formatter={(value: number, name: string) => [`${value}${unit ?? ''}`, name]}
          />
          <Area
            type="monotone" dataKey="avg" name={t('chart.avg')}
            stroke={accent} strokeWidth={2} fill={`url(#grad-${capabilityId})`}
            isAnimationActive={false} dot={false}
          />
        </AreaChart>
      </ResponsiveContainer>
    </Box>
  );
}
