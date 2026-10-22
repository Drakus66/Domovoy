// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { Box, Stack, Typography } from '@mui/material';
import { fmtDateTime } from '../../i18n/format';

/**
 * The component half of the chart kit (see chartKit.ts for the hooks and prop factories):
 * the area-wash gradient defs and the crosshair tooltip readout.
 */

/** Area wash under a trend line: the series hue fading from ~10% to transparent. */
export function AreaGradientDefs({ id, color }: { id: string; color: string }) {
  return (
    <defs>
      <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
        <stop offset="0%" stopColor={color} stopOpacity={0.12} />
        <stop offset="100%" stopColor={color} stopOpacity={0} />
      </linearGradient>
    </defs>
  );
}

interface TooltipEntry {
  value?: number | string;
  name?: string | number;
  dataKey?: string | number;
  stroke?: string;
  color?: string;
}

interface ChartCrosshairTooltipProps {
  unit?: string | null;
  /** Injected by recharts. */
  active?: boolean;
  payload?: TooltipEntry[];
  label?: number | string;
}

/**
 * Tooltip readout for time charts: every series at the hovered X, value leading (strong) and the
 * series name secondary, keyed by a short stroke of the series color. Text wears text tokens only.
 */
export function ChartCrosshairTooltip({ unit, active, payload, label }: ChartCrosshairTooltipProps) {
  if (!active || !payload || payload.length === 0) return null;
  const ts = Number(label);
  return (
    <Box
      sx={{
        bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider',
        borderRadius: 2, px: 1.25, py: 0.75,
      }}
    >
      {Number.isFinite(ts) && (
        <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 0.25 }}>
          {fmtDateTime(new Date(ts).toISOString())}
        </Typography>
      )}
      {payload.map((entry, i) => (
        <Stack key={entry.dataKey ?? entry.name ?? i} direction="row" spacing={0.75} alignItems="center">
          <Box sx={{ width: 12, height: 2, borderRadius: 1, bgcolor: entry.stroke ?? entry.color, flexShrink: 0 }} />
          <Typography variant="body2" sx={{ fontWeight: 700, color: 'text.primary' }}>
            {entry.value}{unit ?? ''}
          </Typography>
          {entry.name != null && (
            <Typography variant="caption" color="text.secondary">{entry.name}</Typography>
          )}
        </Stack>
      ))}
    </Box>
  );
}
