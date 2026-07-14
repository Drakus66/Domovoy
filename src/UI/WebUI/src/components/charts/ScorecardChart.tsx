// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Typography, Skeleton, Stack, Chip, useTheme } from '@mui/material';
import {
  ResponsiveContainer, LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid, Legend,
} from 'recharts';
import { mlApi, Backtest } from '../../api/ml';
import { fmtDateTime, fmtTime } from '../../i18n/format';

interface Props {
  /** Look-back window in days (default 7). */
  days?: number;
  height?: number;
  /** Target capability to score (Epic 2P); default = the server's default target. */
  target?: string;
  /** Model scope to score (Epic 2P): level ("zone" | "zone_kind") + key; default = global. */
  level?: string;
  scopeKey?: string;
}

/**
 * Backtest scorecard (roadmap Epic 2B): overlays the loaded model's prediction against actual telemetry
 * ("prediction vs fact") plus the held-out MAE / training RMSE. The honest signal a reviewer reads before
 * promoting an ML block from Shadow to an active stage (the approval queue itself is Epic 2C).
 */
export default function ScorecardChart({ days = 7, height = 240, target, level, scopeKey }: Props) {
  const { t } = useTranslation('models');
  const theme = useTheme();
  const [data, setData] = useState<Backtest | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setData(null);
    setError(false);
    mlApi
      .backtest(days, target, level, scopeKey)
      .then((b) => { if (!cancelled) setData(b); })
      .catch(() => { if (!cancelled) setError(true); });
    return () => { cancelled = true; };
  }, [days, target, level, scopeKey]);

  if (error) return <Typography variant="caption" color="text.secondary">{t('chart.loadError')}</Typography>;
  if (data === null) return <Skeleton variant="rounded" height={height} />;
  if (!data.model) {
    return <Typography variant="caption" color="text.secondary">{t('chart.noModel')}</Typography>;
  }
  // Enum targets have no numeric series — the class hit-rate is the whole scorecard (Epic 2P).
  if (data.points.length === 0 && data.hitRate != null) {
    return (
      <Stack direction="row" spacing={1}>
        <Chip size="small" variant="outlined" color="primary"
          label={t('chart.chip.hitRate', { value: (data.hitRate * 100).toFixed(0) })} />
        <Chip size="small" variant="outlined"
          label={t('chart.chip.metricScore', { metric: data.model.metric, value: data.model.holdoutScore.toFixed(3) })} />
      </Stack>
    );
  }
  if (data.points.length === 0) {
    return <Typography variant="caption" color="text.secondary">{t('chart.noTelemetry', { days })}</Typography>;
  }

  const series = data.points.map((p) => ({
    t: new Date(p.timestamp).getTime(),
    predicted: p.predicted,
    actual: p.actual,
  }));

  const fmtAxis = (t: number) =>
    fmtTime(t, { month: 'short', day: 'numeric', hour: '2-digit' });

  const accent = theme.palette.primary.main;
  const factColor = theme.palette.text.secondary;

  return (
    <Box>
      <Stack direction="row" spacing={1} mb={1} flexWrap="wrap" useFlexGap>
        <Chip size="small" variant="outlined" label={t('chart.chip.mae', { value: data.model.holdoutMae.toFixed(3) })} />
        <Chip size="small" variant="outlined" label={t('chart.chip.rmse', { value: data.model.rmse.toFixed(3) })} />
        <Chip size="small" variant="outlined" label={t('chart.chip.points', { count: data.points.length, days })} />
      </Stack>
      <Box sx={{ width: '100%', height }}>
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={series} margin={{ top: 4, right: 8, bottom: 0, left: -16 }}>
            <CartesianGrid strokeDasharray="3 3" stroke={theme.palette.divider} vertical={false} />
            <XAxis
              dataKey="t" type="number" scale="time" domain={['dataMin', 'dataMax']}
              tickFormatter={fmtAxis} tick={{ fontSize: 11, fill: theme.palette.text.secondary }}
              minTickGap={48} stroke={theme.palette.divider}
            />
            <YAxis
              width={44} tick={{ fontSize: 11, fill: theme.palette.text.secondary }}
              stroke={theme.palette.divider} domain={['auto', 'auto']}
            />
            <Tooltip
              contentStyle={{
                background: theme.palette.background.paper,
                border: `1px solid ${theme.palette.divider}`,
                borderRadius: 8, fontSize: 12,
              }}
              labelFormatter={(label) => fmtDateTime(new Date(Number(label)).toISOString())}
            />
            <Legend wrapperStyle={{ fontSize: 12 }} />
            <Line type="monotone" dataKey="actual" name={t('chart.legend.actual')} stroke={factColor} strokeWidth={1.5} dot={false} isAnimationActive={false} />
            <Line type="monotone" dataKey="predicted" name={t('chart.legend.predicted')} stroke={accent} strokeWidth={2} dot={false} isAnimationActive={false} />
          </LineChart>
        </ResponsiveContainer>
      </Box>
    </Box>
  );
}
