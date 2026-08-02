// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router-dom';
import {
  Box, Card, CardContent, Chip, IconButton, LinearProgress, Skeleton, Stack,
  ToggleButton, ToggleButtonGroup, Tooltip, Typography,
} from '@mui/material';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded';
import {
  energyApi,
  type EnergyBreakdownResult, type EnergyConsumptionResult, type EnergyCostResult,
} from '../../../api/energy';

/** Energy accent (matches the `energy` device category in deviceVisuals). */
const ACCENT = '#2DD4BF';

/** Period presets → look-back hours + the rollup bucket the consumption query uses. */
const PERIODS: Record<string, { hours: number; bucket: 'hour' | 'day' }> = {
  '24': { hours: 24, bucket: 'hour' },
  '168': { hours: 168, bucket: 'day' },
  '720': { hours: 720, bucket: 'day' },
};

const MAX_ROWS = 6;

/** Breakdown views — by device, by circuit of the electrical tree, or by phase (Epic 3C-D). */
const VIEWS = ['devices', 'circuits', 'phases'] as const;
type View = (typeof VIEWS)[number];

/**
 * Energy dashboard widget (Epic 3C / 3C-D): whole-home cost + kWh for a period, a per-tariff-zone money
 * breakdown, and consumption seen three ways — Top Consumers, per circuit (with what the line's own meter
 * measured but no device explains) and per phase. Not device-scoped: it renders on any custom tab and owns
 * its period toggle. Data comes from the on-the-fly energy endpoints; Sankey flows are a later polish.
 */
export default function EnergyTile() {
  const { t } = useTranslation('dashboards');
  const [period, setPeriod] = useState<keyof typeof PERIODS>('24');
  const [view, setView] = useState<View>('devices');
  const [consumption, setConsumption] = useState<EnergyConsumptionResult | null>(null);
  const [cost, setCost] = useState<EnergyCostResult | null>(null);
  const [breakdown, setBreakdown] = useState<EnergyBreakdownResult | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    const { hours, bucket } = PERIODS[period];
    const from = new Date(Date.now() - hours * 3600_000).toISOString();
    Promise.all([
      energyApi.getConsumption({ from, bucket }),
      energyApi.getCost({ from }),
      // The topology view is only meaningful once the wiring is described; an empty result hides the tabs.
      energyApi.getBreakdown({ from }).catch(() => null),
    ])
      .then(([c, m, b]) => {
        if (cancelled) return;
        setConsumption(c);
        setCost(m);
        setBreakdown(b);
      })
      .catch(() => { /* quiet: the tile shows the empty state on error */ })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [period]);

  // Top Consumers: keep the API's kWh-descending order and cap the rows (devices with accounting turned
  // off never reach the client).
  const deviceRows = useMemo(() => (consumption?.devices ?? []).slice(0, MAX_ROWS), [consumption]);

  const circuits = useMemo(
    () => (breakdown?.nodes ?? []).filter((n) => n.kind === 'circuit')
      .sort((a, b) => b.kwh - a.kwh).slice(0, MAX_ROWS),
    [breakdown],
  );
  const phases = useMemo(() => breakdown?.phases ?? [], [breakdown]);
  // Everything the meters saw but no configured device explains, plus the devices with no circuit at all.
  const unaccountedKwh = useMemo(
    () => (breakdown?.nodes ?? []).reduce((sum, n) => sum + (n.unaccountedKwh ?? 0), 0)
      + (breakdown?.unmappedKwh ?? 0),
    [breakdown],
  );
  const hasTopology = circuits.length > 0 || phases.length > 0;

  // Rows of the active view, as (key, label, kWh) so one renderer covers all three.
  const rows = useMemo(() => {
    if (view === 'circuits') return circuits.map((c) => ({ key: c.nodeId, label: c.name, kwh: c.kwh, tag: null as string | null }));
    if (view === 'phases') return phases.map((p) => ({ key: p.phase, label: p.phase.toUpperCase(), kwh: p.kwh, tag: null as string | null }));
    return deviceRows.map((d) => ({
      key: d.deviceId, label: d.name, kwh: d.kwh, tag: d.energyRole === 'mains' ? t('energy.mains') : null,
    }));
  }, [view, circuits, phases, deviceRows, t]);
  const maxKwh = useMemo(() => Math.max(0.001, ...rows.map((r) => r.kwh)), [rows]);

  const currency = cost?.currency ?? '';
  const zones = cost?.zones ?? [];
  const fmtKwh = (v: number) => `${v.toFixed(v < 10 ? 2 : 1)} ${t('energy.kwh')}`;
  const fmtMoney = (v: number) => `${v.toFixed(2)} ${currency}`.trim();

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" spacing={1} alignItems="center" mb={1.5}>
          <BoltRoundedIcon fontSize="small" sx={{ color: ACCENT }} />
          <Typography variant="subtitle2" fontWeight={700} noWrap flex={1}>
            {t('energy.title')}
          </Typography>
          <ToggleButtonGroup
            size="small" exclusive value={period}
            onChange={(_, v) => { if (v !== null) setPeriod(v); }}
          >
            {Object.keys(PERIODS).map((p) => (
              <ToggleButton key={p} value={p} sx={{ px: 1, py: 0.25 }}>
                {t(`energy.period.${p}`)}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
          <Tooltip title={t('energy.configure')}>
            <IconButton size="small" component={RouterLink} to="/settings">
              <SettingsRoundedIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        </Stack>

        {loading ? (
          <Skeleton variant="rounded" height={160} />
        ) : (
          <Stack spacing={1.5}>
            {/* Headline: money + kWh for the period. */}
            <Stack direction="row" spacing={3}>
              <Box>
                <Typography variant="h5" fontWeight={800}>{fmtMoney(cost?.totalCost ?? 0)}</Typography>
                <Typography variant="caption" color="text.secondary">{t('energy.cost')}</Typography>
              </Box>
              <Box>
                <Typography variant="h5" fontWeight={800}>{fmtKwh(cost?.totalKwh ?? consumption?.consumerTotalKwh ?? 0)}</Typography>
                <Typography variant="caption" color="text.secondary">{t('energy.consumption')}</Typography>
              </Box>
            </Stack>

            {/* Money split by tariff zone (skipped for a flat tariff with just the default zone). */}
            {zones.length > 1 && (
              <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap>
                {zones.map((z) => (
                  <Chip
                    key={z.zone} size="small" variant="outlined"
                    label={`${z.zone}: ${fmtMoney(z.cost)}`}
                  />
                ))}
              </Stack>
            )}

            {/* View switch — only once the electrical topology gives circuits/phases something to show. */}
            {hasTopology && (
              <ToggleButtonGroup
                size="small" exclusive value={view}
                onChange={(_, v) => { if (v !== null) setView(v); }}
              >
                {VIEWS.map((v) => (
                  <ToggleButton key={v} value={v} sx={{ px: 1, py: 0.25 }}>
                    {t(`energy.view.${v}`)}
                  </ToggleButton>
                ))}
              </ToggleButtonGroup>
            )}

            {/* Ranking for the active view (devices / circuits / phases). */}
            {rows.length === 0 ? (
              <Typography variant="body2" color="text.secondary">{t('energy.empty')}</Typography>
            ) : (
              <Box>
                <Typography variant="caption" color="text.secondary">{t(`energy.rankTitle.${view}`)}</Typography>
                <Stack spacing={0.75} mt={0.5}>
                  {rows.map((r) => (
                    <Box key={r.key}>
                      <Stack direction="row" spacing={1} alignItems="center">
                        <Typography variant="body2" noWrap flex={1}>{r.label}</Typography>
                        {r.tag && <Chip label={r.tag} size="small" sx={{ height: 18, fontSize: 10 }} />}
                        <Typography variant="body2" color="text.secondary">{fmtKwh(r.kwh)}</Typography>
                      </Stack>
                      <LinearProgress
                        variant="determinate"
                        value={Math.min(100, (r.kwh / maxKwh) * 100)}
                        sx={{
                          height: 6, borderRadius: 3, mt: 0.25,
                          bgcolor: 'action.hover',
                          '& .MuiLinearProgress-bar': { bgcolor: ACCENT },
                        }}
                      />
                    </Box>
                  ))}
                </Stack>

                {/* What the meters saw but nothing explains — honest about an incompletely mapped house. */}
                {view !== 'devices' && unaccountedKwh > 0.001 && (
                  <Typography variant="caption" color="text.secondary" display="block" mt={0.75}>
                    {t('energy.unaccounted', { value: fmtKwh(unaccountedKwh) })}
                  </Typography>
                )}
              </Box>
            )}
          </Stack>
        )}
      </CardContent>
    </Card>
  );
}
