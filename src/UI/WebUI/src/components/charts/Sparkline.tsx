// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { Box } from '@mui/material';
import { ResponsiveContainer, LineChart, Line, YAxis } from 'recharts';
import type { AggregateBucket } from '../../api/history';

/**
 * Minimal inline trend — a stripped TelemetryChart for a device tile: no axes, grid, tooltip or labels,
 * just the shape of the last N samples. Renders nothing below two points (no trend to show). Data comes
 * pre-loaded from the batch loader (useTelemetryBatch), so the sparkline itself fetches nothing.
 */
export default function Sparkline({
  buckets, color, height = 24,
}: { buckets: AggregateBucket[] | null; color: string; height?: number }) {
  if (!buckets || buckets.length < 2) return null;

  const data = buckets.map((b) => ({ v: b.value }));

  return (
    <Box sx={{ width: '100%', height }} aria-hidden>
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={data} margin={{ top: 2, right: 0, bottom: 2, left: 0 }}>
          <YAxis hide domain={['dataMin', 'dataMax']} />
          <Line
            type="monotone" dataKey="v" stroke={color} strokeWidth={2}
            dot={false} isAnimationActive={false} strokeLinecap="round" strokeLinejoin="round"
          />
        </LineChart>
      </ResponsiveContainer>
    </Box>
  );
}
