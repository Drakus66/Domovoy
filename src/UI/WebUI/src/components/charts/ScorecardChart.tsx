import { useEffect, useState } from 'react';
import { Box, Typography, Skeleton, Stack, Chip, useTheme } from '@mui/material';
import {
  ResponsiveContainer, LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid, Legend,
} from 'recharts';
import { mlApi, Backtest } from '../../api/ml';

interface Props {
  /** Look-back window in days (default 7). */
  days?: number;
  height?: number;
}

/**
 * Backtest scorecard (roadmap Epic 2B): overlays the loaded model's prediction against actual telemetry
 * ("prediction vs fact") plus the held-out MAE / training RMSE. The honest signal a reviewer reads before
 * promoting an ML block from Shadow to an active stage (the approval queue itself is Epic 2C).
 */
export default function ScorecardChart({ days = 7, height = 240 }: Props) {
  const theme = useTheme();
  const [data, setData] = useState<Backtest | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setData(null);
    setError(false);
    mlApi
      .backtest(days)
      .then((b) => { if (!cancelled) setData(b); })
      .catch(() => { if (!cancelled) setError(true); });
    return () => { cancelled = true; };
  }, [days]);

  if (error) return <Typography variant="caption" color="text.secondary">Could not load the backtest.</Typography>;
  if (data === null) return <Skeleton variant="rounded" height={height} />;
  if (!data.model) {
    return <Typography variant="caption" color="text.secondary">No model loaded yet — train one first.</Typography>;
  }
  if (data.points.length === 0) {
    return <Typography variant="caption" color="text.secondary">No telemetry in the last {days}d to score against.</Typography>;
  }

  const series = data.points.map((p) => ({
    t: new Date(p.timestamp).getTime(),
    predicted: p.predicted,
    actual: p.actual,
  }));

  const fmtTime = (t: number) =>
    new Date(t).toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit' });

  const accent = theme.palette.primary.main;
  const factColor = theme.palette.text.secondary;

  return (
    <Box>
      <Stack direction="row" spacing={1} mb={1} flexWrap="wrap" useFlexGap>
        <Chip size="small" variant="outlined" label={`Backtest MAE ${data.model.holdoutMae.toFixed(3)}`} />
        <Chip size="small" variant="outlined" label={`Train RMSE ${data.model.rmse.toFixed(3)}`} />
        <Chip size="small" variant="outlined" label={`${data.points.length} points · ${days}d`} />
      </Stack>
      <Box sx={{ width: '100%', height }}>
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={series} margin={{ top: 4, right: 8, bottom: 0, left: -16 }}>
            <CartesianGrid strokeDasharray="3 3" stroke={theme.palette.divider} vertical={false} />
            <XAxis
              dataKey="t" type="number" scale="time" domain={['dataMin', 'dataMax']}
              tickFormatter={fmtTime} tick={{ fontSize: 11, fill: theme.palette.text.secondary }}
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
              labelFormatter={(t) => new Date(Number(t)).toLocaleString()}
            />
            <Legend wrapperStyle={{ fontSize: 12 }} />
            <Line type="monotone" dataKey="actual" name="actual" stroke={factColor} strokeWidth={1.5} dot={false} isAnimationActive={false} />
            <Line type="monotone" dataKey="predicted" name="predicted" stroke={accent} strokeWidth={2} dot={false} isAnimationActive={false} />
          </LineChart>
        </ResponsiveContainer>
      </Box>
    </Box>
  );
}
