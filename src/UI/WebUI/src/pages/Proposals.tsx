// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import {
  Container, Box, Typography, Stack, Button, LinearProgress, Alert, Card, CardContent, Chip,
  ToggleButtonGroup, ToggleButton, Tooltip,
} from '@mui/material';
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded';
import CancelRoundedIcon from '@mui/icons-material/CancelRounded';
import ScienceRoundedIcon from '@mui/icons-material/ScienceRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import TravelExploreRoundedIcon from '@mui/icons-material/TravelExploreRounded';
import InboxRoundedIcon from '@mui/icons-material/InboxRounded';
import { proposalsApi, Proposal, ProposalKind, ProposalStatus } from '../api/proposals';
import { automationsApi } from '../api/automations';
import { replayApi } from '../api/replay';
import { fmtDateTime } from '../i18n/format';

const kindLabel = (k: ProposalKind): string => i18n.t(`proposals:kind.${k}`);

const stageName = (s?: number | null) =>
  i18n.t(`proposals:stageName.${s === 2 ? 'full' : s === 1 ? 'bounded' : 'shadow'}`);

const statusColor = (s: ProposalStatus) =>
  s === 'Approved' ? 'success' : s === 'Rejected' ? 'default' : 'warning';

export default function Proposals() {
  const { t } = useTranslation('proposals');
  const [proposals, setProposals] = useState<Proposal[]>([]);
  const [filter, setFilter] = useState<'Proposed' | 'all'>('Proposed');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  // Inline 1F replay result per rule proposal, keyed by proposal id.
  const [sims, setSims] = useState<Record<string, string>>({});

  const load = useCallback(async () => {
    setError(null);
    try {
      setProposals(await proposalsApi.list(filter === 'Proposed' ? 'Proposed' : undefined));
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, [filter, t]);

  useEffect(() => { setLoading(true); load(); }, [load]);

  const runProposer = async () => {
    setBusy(true); setError(null); setInfo(null);
    try {
      const r = await proposalsApi.suggest();
      setInfo(t('proposerResult', { created: r.created, candidates: r.candidates, note: r.note }));
      await load();
    } catch {
      setError(t('errors.proposer'));
    } finally {
      setBusy(false);
    }
  };

  const runDiscovery = async () => {
    setBusy(true); setError(null); setInfo(null);
    try {
      const r = await proposalsApi.discover();
      setInfo(t('discoveryResult', { created: r.created, patterns: r.patterns, note: r.note }));
      await load();
    } catch {
      setError(t('errors.discovery'));
    } finally {
      setBusy(false);
    }
  };

  const approve = async (p: Proposal) => {
    setBusy(true); setError(null); setInfo(null);
    try {
      const updated = await proposalsApi.approve(p.id);
      setInfo(t('approved', { title: updated.title, id: updated.decisionId.slice(0, 8) }));
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
      const rule = await automationsApi.getRule(p.ruleId);
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

  const provenance = (p: Proposal): string | null => {
    if (p.kind === 'BlockPromotion') return t('provenance.stage', { from: stageName(p.fromStage), to: stageName(p.toStage) });
    if (p.kind === 'ModelSelection') return t('provenance.pinModel', { version: p.modelVersion ?? 0 });
    if (p.kind === 'MlTask') return t('provenance.mlTask', { target: p.mlTaskTarget ?? '' });
    return null;
  };

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
            <Button variant="outlined" startIcon={<AutoAwesomeRoundedIcon />} onClick={runProposer} disabled={busy}>
              {t('actions.runProposer')}
            </Button>
            <Tooltip title={t('discoveryTooltip')}>
              <Button variant="contained" startIcon={<TravelExploreRoundedIcon />} onClick={runDiscovery} disabled={busy}>
                {t('actions.runDiscovery')}
              </Button>
            </Tooltip>
          </Stack>
        </Stack>

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
              const prov = provenance(p);
              const pending = p.status === 'Proposed';
              return (
                <Card key={p.id} variant="outlined">
                  <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                    <Stack direction="row" alignItems="flex-start" gap={2}>
                      <Box flex={1} minWidth={0}>
                        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.5}>
                          <Typography fontWeight={700}>{p.title}</Typography>
                          <Chip size="small" variant="outlined" label={kindLabel(p.kind)} />
                          <Chip size="small" variant="outlined" label={p.source} />
                          {!pending && (
                            <Chip size="small" color={statusColor(p.status)} label={p.status} />
                          )}
                        </Stack>
                        {p.rationale && (
                          <Typography variant="body2" color="text.secondary">{p.rationale}</Typography>
                        )}
                        <Typography variant="caption" color="text.secondary" display="block" mt={0.5}>
                          {prov && <>{prov} · </>}
                          {p.metric && p.score != null && <>{p.metric} {p.score.toFixed(3)} · </>}
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
                            <Button
                              size="small"
                              variant="outlined"
                              startIcon={<ScienceRoundedIcon />}
                              onClick={() => simulate(p)}
                              disabled={busy}
                            >
                              {t('actions.simulate')}
                            </Button>
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
