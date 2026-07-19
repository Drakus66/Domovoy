// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router-dom';
import {
  Box, Card, CardContent, Stack, Typography, Button, Divider, Link,
} from '@mui/material';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import { activityApi, ActivityEntry } from '../../api/activity';
import { proposalsApi, Proposal } from '../../api/proposals';
import { automationsApi, AutomationRule } from '../../api/automations';
import { proposalTitle } from '../proposals/proposalText';
import HearthMark from '../common/HearthMark';
import { fmtTime } from '../../i18n/format';

const REFRESH_INTERVAL_MS = 60_000;

/**
 * Right rail on the dashboard (roadmap dashboard fill, block B): what the house spirit has done today
 * (recent automation activity) and what awaits a decision (the proposal queue, condensed). Each half
 * fetches independently and hides itself on error, so one failure never blanks the rail. "Later" is a
 * local, session-only dismiss — the proposal keeps its Proposed status server-side and returns next visit.
 */
export default function DomovoyRail({ devices }: { devices: CapabilityDevice[] }) {
  const { t } = useTranslation(['dashboards', 'proposals', 'devices']);
  const [activity, setActivity] = useState<ActivityEntry[] | null>(null);
  const [proposals, setProposals] = useState<Proposal[]>([]);
  const [rules, setRules] = useState<Map<string, AutomationRule>>(new Map());
  const [dismissed, setDismissed] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);

  const nameOf = useCallback(
    (id?: string | null) => (id ? devices.find((d) => d.id === id)?.name ?? id : '—'),
    [devices],
  );

  const loadProposals = useCallback(() => {
    proposalsApi.list('Proposed')
      .then(setProposals)
      .catch(() => undefined);
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
      loadProposals();
    };
    load();
    const interval = setInterval(load, REFRESH_INTERVAL_MS);
    return () => { cancelled = true; clearInterval(interval); };
  }, [loadProposals]);

  const pending = useMemo(
    () => proposals.filter((p) => !dismissed.has(p.id)),
    [proposals, dismissed],
  );

  const approve = async (p: Proposal) => {
    setBusy(true);
    // Optimistic: drop it from the queue immediately, restore on failure.
    setProposals((prev) => prev.filter((x) => x.id !== p.id));
    try {
      await proposalsApi.approve(p.id);
    } catch {
      setProposals((prev) => [p, ...prev]);
    } finally {
      setBusy(false);
    }
  };

  const defer = (p: Proposal) => setDismissed((prev) => new Set(prev).add(p.id));

  return (
    <Box sx={{ width: { xs: '100%', md: 290 }, flexShrink: 0 }}>
      <Stack spacing={2}>
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
