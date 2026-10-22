// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router-dom';
import {
  Box, Card, CardContent, Stack, Typography, Button, Divider, Link,
} from '@mui/material';
import MovieFilterRoundedIcon from '@mui/icons-material/MovieFilterRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import { activityApi, ActivityEntry } from '../../api/activity';
import { proposalsApi, Proposal } from '../../api/proposals';
import { pendingProposals } from '../../store/liveData';
import { automationsApi, AutomationRule } from '../../api/automations';
import { scenesApi, Scene } from '../../api/scenes';
import { energyApi, EnergyCostResult } from '../../api/energy';
import { proposalTitle } from '../proposals/proposalText';
import HearthMark from '../common/HearthMark';
import { fmtTime } from '../../i18n/format';

const REFRESH_INTERVAL_MS = 60_000;

/**
 * Right rail on the dashboard — the everyday "console" (roadmap Epic 3I): one-tap scenes and today's energy
 * up top (the daily, functional cards), then what the house spirit did today (recent automation activity) and
 * what awaits a decision (the proposal queue, condensed and shown only when non-empty). Each panel fetches
 * independently and hides itself on error/emptiness, so one failure never blanks the rail. "Later" is a local,
 * session-only dismiss — the proposal keeps its Proposed status server-side and returns next visit.
 */
export default function DomovoyRail({ devices }: { devices: CapabilityDevice[] }) {
  const { t } = useTranslation(['dashboards', 'proposals', 'devices']);
  const [activity, setActivity] = useState<ActivityEntry[] | null>(null);
  const [rules, setRules] = useState<Map<string, AutomationRule>>(new Map());
  const [scenes, setScenes] = useState<Scene[]>([]);
  const [energy, setEnergy] = useState<EnergyCostResult | null>(null);
  const [activatingScene, setActivatingScene] = useState<string | null>(null);
  const [dismissed, setDismissed] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);

  const nameOf = useCallback(
    (id?: string | null) => (id ? devices.find((d) => d.id === id)?.name ?? id : '—'),
    [devices],
  );

  const loadRules = useCallback(() => {
    automationsApi.getRules()
      .then((rs) => setRules(new Map(rs.map((r) => [r.id, r]))))
      .catch(() => undefined);
  }, []);

  useEffect(() => {
    let cancelled = false;
    const load = () => {
      const midnight = new Date();
      midnight.setHours(0, 0, 0, 0);
      activityApi.get({ source: 'automation', from: midnight.toISOString(), limit: 6 })
        .then((rows) => { if (!cancelled) setActivity(rows); })
        .catch(() => { if (!cancelled) setActivity((prev) => prev ?? []); });
      energyApi.getCost({ from: midnight.toISOString() })
        .then((c) => { if (!cancelled) setEnergy(c); })
        .catch(() => undefined);
      loadRules();
    };
    load();
    scenesApi.getScenes().then((s) => { if (!cancelled) setScenes(s); }).catch(() => undefined);
    const interval = setInterval(load, REFRESH_INTERVAL_MS);
    return () => { cancelled = true; clearInterval(interval); };
  }, [loadRules]);

  const activateScene = async (scene: Scene) => {
    setActivatingScene(scene.id);
    try { await scenesApi.activate(scene.id); } catch { /* transient — ignore */ } finally { setActivatingScene(null); }
  };

  // Очередь предложений — общий ресурс: её же читают навигация, очаг в шапке и дайджест.
  const proposals = pendingProposals.use().data ?? [];

  const pending = useMemo(
    () => proposals.filter((p) => !dismissed.has(p.id)),
    [proposals, dismissed],
  );

  const approve = async (p: Proposal) => {
    setBusy(true);
    // Оптимистично убираем из очереди сразу — и, поскольку очередь общая, badge в навигации и очаг
    // в шапке гаснут тем же движением; при ошибке возвращаем и перечитываем с сервера.
    pendingProposals.set((prev) => prev?.filter((x) => x.id !== p.id));
    try {
      await proposalsApi.approve(p.id);
    } catch {
      await pendingProposals.refresh();
    } finally {
      setBusy(false);
    }
  };

  const defer = (p: Proposal) => setDismissed((prev) => new Set(prev).add(p.id));

  return (
    <Box sx={{ width: { xs: '100%', md: 290 }, flexShrink: 0 }}>
      <Stack spacing={2}>
        {/* Everyday console (Epic 3I): one-tap scenes — the daily control the rail was missing. */}
        {scenes.length > 0 && (
          <Card variant="outlined">
            <CardContent sx={{ '&:last-child': { pb: 2 } }}>
              <Typography variant="subtitle2" mb={1.5}>{t('rail.scenes')}</Typography>
              <Stack direction="row" flexWrap="wrap" useFlexGap spacing={1}>
                {scenes.slice(0, 4).map((s) => (
                  <Button
                    key={s.id}
                    size="small"
                    variant="outlined"
                    color="inherit"
                    startIcon={<MovieFilterRoundedIcon fontSize="small" />}
                    disabled={activatingScene === s.id}
                    onClick={() => activateScene(s)}
                  >
                    {s.name}
                  </Button>
                ))}
              </Stack>
              {scenes.length > 4 && (
                <Box mt={1}>
                  <Link component={RouterLink} to="/scenes" variant="body2" underline="hover">
                    {t('rail.moreScenes')}
                  </Link>
                </Box>
              )}
            </CardContent>
          </Card>
        )}

        {/* Everyday console (Epic 3I): today's energy — functional, grown-up daily info. Hidden with no usage. */}
        {energy && energy.totalKwh > 0 && (
          <Card variant="outlined">
            <CardContent sx={{ '&:last-child': { pb: 2 } }}>
              <Stack direction="row" alignItems="center" spacing={1} mb={1}>
                <BoltRoundedIcon fontSize="small" color="disabled" />
                <Typography variant="subtitle2">{t('rail.energyToday')}</Typography>
              </Stack>
              <Stack direction="row" alignItems="baseline" spacing={1}>
                <Typography variant="h5" fontWeight={700}>
                  {energy.totalCost.toFixed(energy.totalCost >= 100 ? 0 : 1)} {energy.currency}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  · {energy.totalKwh.toFixed(1)} {t('rail.kwh')}
                </Typography>
              </Stack>
            </CardContent>
          </Card>
        )}

        {/* Panel 1 — what the house spirit did today. */}
        <Card variant="outlined">
          <CardContent sx={{ '&:last-child': { pb: 2 } }}>
            <Stack direction="row" alignItems="center" spacing={1} mb={1.5}>
              <HearthMark size={16} />
              <Typography variant="subtitle2">{t('rail.today')}</Typography>
            </Stack>

            {activity && activity.length > 0 ? (
              <Stack spacing={1.5}>
                {activity.map((e, i) => (
                  <Stack key={i} direction="row" spacing={1.25} alignItems="baseline">
                    <Typography
                      variant="caption"
                      sx={{ fontFamily: 'monospace', color: 'text.disabled', flexShrink: 0, width: 36 }}
                    >
                      {fmtTime(e.timestamp, { hour: '2-digit', minute: '2-digit' })}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">{e.title}</Typography>
                  </Stack>
                ))}
              </Stack>
            ) : (
              <Typography variant="body2" color="text.secondary">{t('rail.empty')}</Typography>
            )}

            <Box mt={1.5}>
              <Link component={RouterLink} to="/logs" variant="body2" fontWeight={600} underline="hover">
                {t('rail.history')}
              </Link>
            </Box>
          </CardContent>
        </Card>

        {/* Panel 2 — the approval queue (hidden when nothing is pending). */}
        {pending.length > 0 && (
          <Card variant="outlined" sx={{ borderColor: (th) => `${th.palette.primary.main}4D` }}>
            <CardContent sx={{ '&:last-child': { pb: 2 } }}>
              <Typography variant="subtitle2" color="primary.main" mb={1.5}>
                {t('rail.pending')} · {pending.length}
              </Typography>
              <Stack divider={<Divider flexItem />} spacing={1.5}>
                {pending.map((p) => (
                  <Box key={p.id}>
                    <Typography variant="body2" color="text.secondary" mb={1}>
                      {proposalTitle(p, p.ruleId ? rules.get(p.ruleId) : undefined, nameOf)}
                    </Typography>
                    <Stack direction="row" spacing={1}>
                      <Button size="small" variant="contained" disabled={busy} onClick={() => approve(p)}>
                        {t('rail.approve')}
                      </Button>
                      <Button size="small" variant="outlined" color="inherit" onClick={() => defer(p)}>
                        {t('rail.defer')}
                      </Button>
                    </Stack>
                  </Box>
                ))}
              </Stack>
            </CardContent>
          </Card>
        )}
      </Stack>
    </Box>
  );
}
