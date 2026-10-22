// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Dialog, DialogContent, DialogTitle, IconButton, Stack, ToggleButton, ToggleButtonGroup,
  Typography, useMediaQuery, useTheme,
} from '@mui/material';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import type { EnergyBreakdownResult } from '../../../api/energy';
import HierarchySunburst, { type SunburstNode } from '../../charts/HierarchySunburst';
import { usePowerSunburst } from './usePowerSunburst';
import type { PowerDeviceRow, PowerMetric } from './powerSunburst';

/**
 * Full-size drill-down of the electrical tree (supply → panels → circuits → devices): the tile's
 * "scheme" view grown into a dialog, plus the watts/kWh metric switch the tile keeps hidden.
 */
export default function PowerSunburstDialog({
  open, onClose, breakdown, devices, period, periods, onPeriodChange,
}: {
  open: boolean;
  onClose: () => void;
  breakdown: EnergyBreakdownResult | null;
  devices: PowerDeviceRow[];
  period: string;
  periods: string[];
  onPeriodChange: (p: string) => void;
}) {
  const { t } = useTranslation('dashboards');
  const theme = useTheme();
  const narrow = useMediaQuery(theme.breakpoints.down('sm'));
  const [metric, setMetric] = useState<PowerMetric>('kwh');
  const [current, setCurrent] = useState<SunburstNode | null>(null);

  const { root, valueFormatter, hintFormatter } = usePowerSunburst(breakdown, devices, metric);

  const detail = useMemo(() => {
    const meta = (current ?? root)?.meta;
    if (!meta || metric !== 'kwh') return null;
    const parts: string[] = [];
    const meter = meta.meterKwh as number | null | undefined;
    const unaccounted = meta.unaccountedKwh as number | null | undefined;
    if (meter != null) parts.push(t('energy.scheme.meter', { value: valueFormatter(meter) }));
    if (unaccounted != null && unaccounted > 0.001) {
      parts.push(t('energy.scheme.unaccountedValue', { value: valueFormatter(unaccounted) }));
    }
    return parts.length > 0 ? parts.join(' · ') : null;
  }, [current, root, metric, t, valueFormatter]);

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle sx={{ pr: 6 }}>
        <Stack direction="row" alignItems="center" spacing={1.5} flexWrap="wrap" useFlexGap>
          <Typography variant="h6" component="span" flex={1} noWrap>
            {t('energy.scheme.title')}
          </Typography>
          <ToggleButtonGroup
            size="small" exclusive value={period}
            onChange={(_, v) => { if (v !== null) onPeriodChange(v); }}
          >
            {periods.map((p) => (
              <ToggleButton key={p} value={p} sx={{ px: 1, py: 0.25 }}>
                {t(`energy.period.${p}`)}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
          <ToggleButtonGroup
            size="small" exclusive value={metric}
            onChange={(_, v) => { if (v !== null) setMetric(v); }}
          >
            <ToggleButton value="kwh" sx={{ px: 1, py: 0.25 }}>{t('energy.scheme.metricKwh')}</ToggleButton>
            <ToggleButton value="watts" sx={{ px: 1, py: 0.25 }}>{t('energy.scheme.metricWatts')}</ToggleButton>
          </ToggleButtonGroup>
        </Stack>
        <IconButton
          onClick={onClose}
          aria-label={t('common:actions.close')}
          sx={{ position: 'absolute', top: 8, right: 8 }}
          size="small"
        >
          <CloseRoundedIcon fontSize="small" />
        </IconButton>
      </DialogTitle>
      <DialogContent>
        {/* A zero-total tree (no telemetry in the period) would render degenerate arcs. */}
        {root && root.value > 0 ? (
          <>
            <HierarchySunburst
              root={root}
              size={narrow ? 300 : 440}
              valueFormatter={valueFormatter}
              hintFormatter={hintFormatter}
              onCurrentChange={setCurrent}
            />
            {detail && (
              <Typography variant="caption" color="text.secondary" display="block" textAlign="center">
                {detail}
              </Typography>
            )}
          </>
        ) : (
          <Typography variant="body2" color="text.secondary">{t('energy.empty')}</Typography>
        )}
      </DialogContent>
    </Dialog>
  );
}
