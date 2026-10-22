// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useTheme } from '@mui/material';
import type { EnergyBreakdownResult } from '../../../api/energy';
import type { SunburstNode } from '../../charts/HierarchySunburst';
import { buildPowerSunburst, type PowerDeviceRow, type PowerMetric } from './powerSunburst';

/**
 * Everything the electrical sunburst needs, derived once: the assembled tree (colors by phase —
 * L1/L2/L3 as three theme hues; gray strictly for "unaccounted"/"unassigned"), the metric
 * formatter and the hover hint. Shared between the EnergyTile scheme view and the dialog.
 */
export function usePowerSunburst(
  breakdown: EnergyBreakdownResult | null,
  devices: PowerDeviceRow[],
  metric: PowerMetric,
) {
  const theme = useTheme();
  const { t } = useTranslation(['dashboards', 'settings']);

  const root = useMemo(() => {
    if (!breakdown) return null;
    return buildPowerSunburst(breakdown, devices, metric, {
      l1: theme.palette.primary.main,
      l2: theme.palette.secondary.main,
      l3: theme.palette.info.main,
      rest: theme.palette.primary.main,
      neutral: theme.palette.text.disabled,
    }, {
      root: t('dashboards:energy.scheme.root'),
      unaccounted: t('dashboards:energy.scheme.unaccounted'),
      unmapped: t('dashboards:energy.scheme.unmapped'),
    });
  }, [breakdown, devices, metric, theme, t]);

  const valueFormatter = useMemo(() => (
    metric === 'kwh'
      ? (v: number) => `${v.toFixed(v < 10 ? 2 : 1)} ${t('dashboards:energy.kwh')}`
      : (v: number) => t('dashboards:band.watts', { value: Math.round(v) })
  ), [metric, t]);

  const hintFormatter = useMemo(() => (node: SunburstNode): string | null => {
    const meta = node.meta ?? {};
    const kind = meta.kind as string | undefined;
    const parts: string[] = [];
    if (kind === 'device') parts.push(t('dashboards:energy.scheme.device'));
    else if (kind === 'supply' || kind === 'panel' || kind === 'circuit') {
      parts.push(t(`settings:powerTopology.kinds.${kind}`));
    }
    const phase = meta.phase as string | null | undefined;
    if (phase) parts.push(t(`settings:powerTopology.phases.${phase}`, phase.toUpperCase()));
    const limit = meta.limitWatts as number | null | undefined;
    if (limit) parts.push(t('dashboards:energy.scheme.limit', { value: Math.round(limit) }));
    return parts.length > 0 ? parts.join(' · ') : null;
  }, [t]);

  return { root, valueFormatter, hintFormatter };
}
