// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router-dom';
import {
  Container, Box, Typography, Stack, Button, LinearProgress, Alert, Card, CardContent, Chip,
  ToggleButtonGroup, ToggleButton, Tooltip, IconButton, Collapse, Link,
} from '@mui/material';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import CancelRoundedIcon from '@mui/icons-material/CancelRounded';
import ScienceRoundedIcon from '@mui/icons-material/ScienceRounded';
import RadarRoundedIcon from '@mui/icons-material/RadarRounded';
import HelpOutlineRoundedIcon from '@mui/icons-material/HelpOutlineRounded';
import InboxRoundedIcon from '@mui/icons-material/InboxRounded';
import RuleRoundedIcon from '@mui/icons-material/RuleRounded';
import TrendingUpRoundedIcon from '@mui/icons-material/TrendingUpRounded';
import PushPinRoundedIcon from '@mui/icons-material/PushPinRounded';
import SchoolRoundedIcon from '@mui/icons-material/SchoolRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import AutoDeleteRoundedIcon from '@mui/icons-material/AutoDeleteRounded';
import { proposalsApi, Proposal, ProposalKind, ProposalStatus } from '../api/proposals';
import { automationsApi, AutomationRule } from '../api/automations';
import { capabilityDevicesApi } from '../api/capabilityDevices';
import { replayApi } from '../api/replay';
import { fmtDateTime } from '../i18n/format';
import { proposalTitle, effectText, evidenceText, sourceKey } from '../components/proposals/proposalText';

const KIND_ICONS: Record<ProposalKind, typeof RuleRoundedIcon> = {
  Rule: RuleRoundedIcon,
  BlockPromotion: TrendingUpRoundedIcon,
  ModelSelection: PushPinRoundedIcon,
  MlTask: SchoolRoundedIcon,
  Scene: AutoAwesomeRoundedIcon,
  RuleAmendment: AutoDeleteRoundedIcon,
};

const statusColor = (s: ProposalStatus) =>
  s === 'Approved' ? 'success' : s === 'Rejected' ? 'default' : 'warning';

// Deep-link to the proposal's target so the reviewer can inspect it before deciding.
const targetLink = (p: Proposal): { to: string; label: string } | null => {
  if ((p.kind === 'Rule' || p.kind === 'RuleAmendment') && p.ruleId)
    return { to: `/automations?focus=${p.ruleId}`, label: 'actions.openRule' };
  if ((p.kind === 'BlockPromotion' || p.kind === 'ModelSelection') && p.blockId)
    return { to: `/blocks?focus=${p.blockId}`, label: 'actions.openBlock' };
  if (p.kind === 'MlTask') return { to: '/models', label: 'actions.openMl' };
  return null;
};

/**
 * The approval queue (Epic 2C): every change the house would like to make — mined rules (2C/2F),
 * ML block promotions/pins, training-task suggestions (2P) — waits here for an explicit human
 * decision. Cards are rendered in the user's language from structured data (the referenced rule +
 * `evidence` numbers), with the concrete approve side-effect spelled out per kind.
 */
export default function Proposals() {
  // 'devices' is loaded alongside: proposalText resolves capability labels from that namespace.
  const { t } = useTranslation(['proposals', 'devices']);
  const [proposals, setProposals] = useState<Proposal[]>([]);
  const [rules, setRules] = useState<Map<string, AutomationRule>>(new Map());
  const [deviceNames, setDeviceNames] = useState<Map<string, string>>(new Map());
  const [filter, setFilter] = useState<'Proposed' | 'all'>('Proposed');
  const [helpOpen, setHelpOpen] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  // Inline 1F replay result per rule proposal, keyed by proposal id.
  const [sims, setSims] = useState<Record<string, string>>({});

  const load = useCallback(async () => {
    setError(null);
    try {
      const [rows, allRules, devices] = await Promise.all([
        proposalsApi.list(filter === 'Proposed' ? 'Proposed' : undefined),
        automationsApi.getRules().catch(() => [] as AutomationRule[]),
        capabilityDevicesApi.getDevices().catch(() => []),
      ]);
      setProposals(rows);
      setRules(new Map(allRules.map((r) => [r.id, r])));
      setDeviceNames(new Map(devices.map((d) => [d.id, d.name || d.id])));
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, [filter, t]);

  useEffect(() => { setLoading(true); load(); }, [load]);

  const nameOf = useCallback(
    (id?: string | null) => (id ? deviceNames.get(id) ?? id : '—'),
    [deviceNames],
  );

  // One button, all three scanners (2C heuristic, 2F discovery, 2P ML-task scan) — the user shouldn't
  // have to know which internal engine finds what; failures of individual scanners are reported, not fatal.
  const scanNow = async () => {
    setBusy(true); setError(null); setInfo(null);
    const [suggest, discover, tasks] = await Promise.allSettled([
      proposalsApi.suggest(), proposalsApi.discover(), proposalsApi.suggestTasks(),
    ]);
    const failed = [suggest, discover, tasks].filter((r) => r.status === 'rejected').length;
    const created = (r: PromiseSettledResult<{ created: number }>) =>
      r.status === 'fulfilled' ? r.value.created : 0;
    const total = created(suggest) + created(discover) + created(tasks);
    if (failed === 3) {
      setError(t('errors.scan'));
    } else if (failed > 0) {
      setInfo(t('scanPartial', { failed, created: total }));
    } else if (total === 0) {
      setInfo(t('scanNothing'));
    } else {
      setInfo(t('scanResult', { suggested: created(suggest), discovered: created(discover), tasks: created(tasks) }));
    }
    await load();
    setBusy(false);
  };

  const approve = async (p: Proposal) => {
    setBusy(true); setError(null); setInfo(null);
    try {
      const updated = await proposalsApi.approve(p.id);
      setInfo(t('approved', { title: proposalTitle(updated, rules.get(updated.ruleId ?? ''), nameOf), id: updated.decisionId.slice(0, 8) }));
      await load();
    } catch {
      setError(t('errors.approve'));
    } finally {
      setBusy(false);
    }
  };

  const reject = async (p: Proposal) => {
    setBusy(true); setError(null); setInfo(null);
    try {
      await proposalsApi.reject(p.id);
      await load();
    } catch {
      setError(t('errors.reject'));
    } finally {
      setBusy(false);
    }
  };

  // Rule proposals carry a Proposed AutomationRule; dry-run it over history (1F) so the reviewer sees when it
  // would have fired before approving.
  const simulate = async (p: Proposal) => {
    if (!p.ruleId) return;
    setBusy(true); setError(null);
    try {
      const rule = rules.get(p.ruleId) ?? await automationsApi.getRule(p.ruleId);
      const result = await replayApi.run(rule, 7);
      setSims((prev) => ({
        ...prev,
        [p.id]: t('simResult', { count: result.fires, events: result.eventsScanned })
          + (result.notes.length ? t('simNotes', { notes: result.notes.join('; ') }) : ''),
      }));
    } catch {
      setError(t('errors.simulate'));
    } finally {
      setBusy(false);
    }
  };

  const sourceChips = useMemo(() => ['ml_proposer', 'discovery', 'ml_task_scanner', 'user'], []);

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={2} flexWrap="wrap" useFlexGap>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t('caption')}
            </Typography>
          </Box>
          <Stack direction="row" spacing={1} alignItems="center">
            <ToggleButtonGroup
              size="small"
              exclusive
              value={filter}
              onChange={(_, v) => v && setFilter(v)}
            >
              <ToggleButton value="Proposed">{t('filter.pending')}</ToggleButton>
              <ToggleButton value="all">{t('filter.all')}</ToggleButton>
            </ToggleButtonGroup>
            <Tooltip title={t('actions.scanNowHint')}>
              <Button variant="contained" startIcon={<RadarRoundedIcon />} onClick={scanNow} disabled={busy}>
                {t('actions.scanNow')}
              </Button>
            </Tooltip>
            <Tooltip title={t('actions.help')}>
              <IconButton
                aria-label={t('actions.help')}
                onClick={() => setHelpOpen((v) => !v)}
                color={helpOpen ? 'primary' : 'default'}
              >
                <HelpOutlineRoundedIcon />
              </IconButton>
            </Tooltip>
          </Stack>
        </Stack>

        <Collapse in={helpOpen} unmountOnExit>
          <Card variant="outlined" sx={{ mb: 2, bgcolor: 'action.hover' }}>
            <CardContent>
              <Typography variant="body2" mb={1.5}>{t('help.what')}</Typography>
              <Typography variant="subtitle2" mb={0.5}>{t('help.sourcesTitle')}</Typography>
              <Stack spacing={0.75} mb={1.5}>
                {sourceChips.map((s) => (
                  <Stack key={s} direction="row" spacing={1} alignItems="baseline">
                    <Chip size="small" variant="outlined" label={t(`source.${s}`)} sx={{ flexShrink: 0 }} />
                    <Typography variant="body2" color="text.secondary">{t(`help.sources.${s}`)}</Typography>
                  </Stack>
                ))}
              </Stack>
              <Typography variant="body2" color="text.secondary" mb={0.5}>{t('help.schedule')}</Typography>
              <Typography variant="body2" color="text.secondary">{t('help.review')}</Typography>
            </CardContent>
          </Card>
        </Collapse>

        {(loading || busy) && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
        {info && <Alert severity="info" sx={{ mb: 2 }} onClose={() => setInfo(null)}>{info}</Alert>}

        {proposals.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <InboxRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">
              {filter === 'Proposed' ? t('empty.pending') : t('empty.all')}
            </Typography>
          </Box>
        ) : (
          <Stack spacing={1.5}>
            {proposals.map((p) => {
              const pending = p.status === 'Proposed';
              const KindIcon = KIND_ICONS[p.kind] ?? RuleRoundedIcon;
              const src = sourceKey(p.source);
              const evidence = evidenceText(p);
              const link = targetLink(p);
              return (
                <Card key={p.id} variant="outlined">
                  <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                    <Stack direction="row" alignItems="flex-start" gap={2}>
                      <KindIcon sx={{ color: 'text.secondary', mt: 0.5 }} />
                      <Box flex={1} minWidth={0}>
                        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.5}>
                          <Typography fontWeight={700}>
                            {proposalTitle(p, p.ruleId ? rules.get(p.ruleId) : undefined, nameOf)}
                          </Typography>
                          <Chip size="small" variant="outlined" label={t(`kind.${p.kind}`)} />
                          <Tooltip title={t(`sourceHint.${src}`, { source: p.source })}>
                            <Chip size="small" variant="outlined" label={t(`source.${src}`, { source: p.source })} />
                          </Tooltip>
                          {!pending && (
                            <Chip size="small" color={statusColor(p.status)} label={t(`status.${p.status}`)} />
                          )}
                        </Stack>
                        {evidence && (
                          <Typography variant="body2" color="text.secondary">{evidence}</Typography>
                        )}
                        {pending && (
                          <Typography variant="body2" mt={0.5}>
                            <strong>{t('effect.prefix')}</strong> {effectText(p)}
                            {link && (
                              <> <Link component={RouterLink} to={link.to}>{t(link.label)}</Link></>
                            )}
                          </Typography>
                        )}
                        <Typography variant="caption" color="text.secondary" display="block" mt={0.5}>
                          {t('meta.proposed', { when: fmtDateTime(p.createdAt) })}
                          {p.decidedAt && <> · {t('meta.decided', { when: fmtDateTime(p.decidedAt) })}</>}
                          {p.decisionId && <> · {t('meta.decision', { id: p.decisionId.slice(0, 8) })}</>}
                        </Typography>
                        {sims[p.id] && (
                          <Alert severity="info" sx={{ mt: 1, py: 0 }}>{sims[p.id]}</Alert>
                        )}
                      </Box>
                      {pending && (
                        <Stack spacing={1} alignItems="stretch" sx={{ flexShrink: 0 }}>
                          {p.kind === 'Rule' && p.ruleId && (
                            <Tooltip title={t('actions.simulateHint')}>
                              <Button
                                size="small"
                                variant="outlined"
                                startIcon={<ScienceRoundedIcon />}
                                onClick={() => simulate(p)}
                                disabled={busy}
                              >
                                {t('actions.simulate')}
                              </Button>
                            </Tooltip>
                          )}
                          <Button
                            size="small"
                            variant="contained"
                            color="success"
                            startIcon={<CheckCircleRoundedIcon />}
                            onClick={() => approve(p)}
                            disabled={busy}
                          >
                            {t('actions.approve')}
                          </Button>
                          <Tooltip title={t('rejectTooltip')}>
                            <Button
                              size="small"
                              variant="text"
                              color="inherit"
                              startIcon={<CancelRoundedIcon />}
                              onClick={() => reject(p)}
                              disabled={busy}
                            >
                              {t('actions.reject')}
                            </Button>
                          </Tooltip>
                        </Stack>
                      )}
                    </Stack>
                  </CardContent>
                </Card>
              );
            })}
          </Stack>
        )}
      </Box>
    </Container>
  );
}
