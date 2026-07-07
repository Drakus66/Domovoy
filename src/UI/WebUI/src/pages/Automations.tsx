// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import { fmtDateTime } from '../i18n/format';
import {
  Container, Box, Typography, Stack, Button, IconButton, LinearProgress, Alert,
  Card, CardContent, Tooltip, Chip, Dialog, DialogTitle, DialogContent,
  DialogActions, TextField, MenuItem, FormControlLabel, Checkbox, Divider, Drawer,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import ShieldRoundedIcon from '@mui/icons-material/ShieldRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import ScienceRoundedIcon from '@mui/icons-material/ScienceRounded';
import {
  automationsApi, AutomationRule, AutoHistoryEntry, RuleTrigger, RuleCondition, RuleAction, NewRule, RuleStatus,
} from '../api/automations';
import { replayApi, ReplayResult } from '../api/replay';
import { capabilityDevicesApi, CapabilityDevice } from '../api/capabilityDevices';

const OPERATORS = ['eq', 'ne', 'gt', 'lt', 'gte', 'lte', 'changed'];
// User-selectable lifecycle (Proposed/Approved are reserved for ML proposals, Epic 1F/Phase 2).
// BoundedActive (Epic 1F) = active but rate-limited — the stage between Shadow and full Active.
const STATUS_OPTIONS: RuleStatus[] = ['Active', 'BoundedActive', 'Shadow', 'Disabled'];
const statusColor = (s: RuleStatus): 'success' | 'info' | 'warning' | 'default' =>
  s === 'Active' ? 'success' : s === 'BoundedActive' ? 'warning' : s === 'Shadow' ? 'info' : 'default';

const parseValue = (raw: string): unknown => {
  const s = raw.trim();
  if (s === '') return true;
  if (s.toLowerCase() === 'true') return true;
  if (s.toLowerCase() === 'false') return false;
  const n = Number(s);
  return Number.isNaN(n) ? s : n;
};

const fmt = (v: unknown): string => (typeof v === 'boolean' ? (v ? 'on' : 'off') : String(v ?? ''));

interface DraftState {
  name: string;
  trigDevice: string; trigCap: string; trigOp: string; trigValue: string;
  onlyDark: boolean;
  actDevice: string; actCap: string; actValue: string;
  autoOffSeconds: number;
  status: RuleStatus;
}

const EMPTY_DRAFT: DraftState = {
  name: '', trigDevice: '', trigCap: '', trigOp: 'eq', trigValue: 'true',
  onlyDark: false, actDevice: '', actCap: '', actValue: 'true', autoOffSeconds: 0,
  status: 'Active',
};

export default function Automations() {
  const { t } = useTranslation('automations');
  const [rules, setRules] = useState<AutomationRule[]>([]);
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [draft, setDraft] = useState<DraftState | null>(null);
  const [historyFor, setHistoryFor] = useState<AutomationRule | 'all' | null>(null);
  const [simulateFor, setSimulateFor] = useState<AutomationRule | null>(null);

  const deviceName = useCallback(
    (id?: string | null) => devices.find((d) => d.id === id)?.name ?? id ?? '—',
    [devices],
  );

  const load = useCallback(async () => {
    setError(null);
    try {
      const [r, d] = await Promise.all([automationsApi.getRules(), capabilityDevicesApi.getDevices()]);
      setRules(r);
      setDevices(d);
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  const changeStatus = async (rule: AutomationRule, next: RuleStatus) => {
    if (next === rule.status) return;
    setRules((prev) => prev.map((x) => (x.id === rule.id ? { ...x, status: next } : x)));
    try { await automationsApi.setStatus(rule.id, next); }
    catch { setError(t('errors.changeStatus')); load(); }
  };

  const remove = async (rule: AutomationRule) => {
    if (!window.confirm(t('confirm.delete', { name: rule.name }))) return;
    try { await automationsApi.deleteRule(rule.id); await load(); }
    catch { setError(t('errors.delete')); }
  };

  const triggerText = useCallback((t: RuleTrigger): string => {
    if (t.type === 'DeviceState') return `${t.capabilityId ?? i18n.t('automations:trigger.any')} ${t.operator ?? 'eq'} ${fmt(t.value)} · ${deviceName(t.deviceId)}`;
    if (t.type === 'Time') return i18n.t('automations:trigger.schedule', { cron: t.cron ?? '' });
    return `${t.sun ?? i18n.t('automations:trigger.sun')}${t.offsetMinutes ? ` ${t.offsetMinutes > 0 ? '+' : ''}${t.offsetMinutes}m` : ''}`;
  }, [deviceName]);

  const conditionText = (c: RuleCondition): string => {
    if (c.type === 'Sun') return c.dark === false ? i18n.t('automations:condition.whileLight') : i18n.t('automations:condition.whileDark');
    if (c.type === 'TimeOfDay') return `${c.fromTime}–${c.toTime}`;
    if (c.type === 'Mode') return i18n.t('automations:condition.mode', { mode: c.mode });
    return `${c.capabilityId} ${c.operator ?? 'eq'} ${fmt(c.value)}`;
  };

  const actionText = useCallback((a: RuleAction): string => {
    if (a.type === 'Command') return `${i18n.t('automations:action.set', { assignments: Object.entries(a.set ?? {}).map(([k, v]) => `${k}=${fmt(v)}`).join(', ') })} · ${deviceName(a.deviceId)}`;
    if (a.type === 'Delay') return i18n.t('automations:action.wait', { seconds: a.delaySeconds });
    return i18n.t('automations:action.notify', { message: a.message ?? '' });
  }, [deviceName]);

  const save = async () => {
    if (!draft || !draft.name.trim() || !draft.trigDevice || !draft.trigCap || !draft.actDevice || !draft.actCap) return;

    const triggers: RuleTrigger[] = [{
      type: 'DeviceState', deviceId: draft.trigDevice, capabilityId: draft.trigCap,
      operator: draft.trigOp, value: draft.trigOp === 'changed' ? null : parseValue(draft.trigValue),
    }];
    const conditions: RuleCondition[] = draft.onlyDark ? [{ type: 'Sun', dark: true }] : [];
    const actions: RuleAction[] = [{ type: 'Command', deviceId: draft.actDevice, set: { [draft.actCap]: parseValue(draft.actValue) } }];
    if (draft.autoOffSeconds > 0) {
      actions.push({ type: 'Delay', delaySeconds: draft.autoOffSeconds });
      actions.push({ type: 'Command', deviceId: draft.actDevice, set: { [draft.actCap]: false } });
    }

    const rule: NewRule = { name: draft.name.trim(), description: null, status: draft.status, triggers, conditions, actions };
    try {
      await automationsApi.createRule(rule);
      setDraft(null);
      await load();
    } catch {
      setError(t('errors.create'));
    }
  };

  const sorted = useMemo(
    () => [...rules].sort((a, b) => Number(b.isProtected) - Number(a.isProtected) || a.name.localeCompare(b.name)),
    [rules],
  );

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={3}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t('subtitle')}
            </Typography>
          </Box>
          <Stack direction="row" spacing={1}>
            <Button startIcon={<HistoryRoundedIcon />} onClick={() => setHistoryFor('all')}>{t('actions.history')}</Button>
            <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={() => setDraft({ ...EMPTY_DRAFT })}>
              {t('actions.newRule')}
            </Button>
          </Stack>
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {sorted.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <BoltRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">
              {t('empty.none')}
            </Typography>
          </Box>
        ) : (
          <Stack spacing={1.5}>
            {sorted.map((rule) => (
              <Card key={rule.id} variant="outlined">
                <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                  <Stack direction="row" alignItems="flex-start" spacing={2}>
                    <Box flex={1} minWidth={0}>
                      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.5}>
                        <Typography fontWeight={700}>{rule.name}</Typography>
                        {rule.isProtected && (
                          <Chip size="small" icon={<ShieldRoundedIcon />} label={t('chips.protected')} color="warning" variant="outlined" />
                        )}
                        <Chip size="small" label={t(`status.${rule.status}`)}
                          color={statusColor(rule.status)} variant="outlined" />
                      </Stack>
                      <Typography variant="body2" color="text.secondary">
                        <b>{t('rule.when')}</b> {rule.triggers.map(triggerText).join(' or ')}
                      </Typography>
                      {rule.conditions.length > 0 && (
                        <Typography variant="body2" color="text.secondary">
                          <b>{t('rule.if')}</b> {rule.conditions.map(conditionText).join(' and ')}
                        </Typography>
                      )}
                      <Typography variant="body2" color="text.secondary">
                        <b>{t('rule.then')}</b> {rule.actions.map(actionText).join(' → ')}
                      </Typography>
                    </Box>
                    <Stack direction="row" alignItems="center" spacing={0.5}>
                      <Tooltip title={t('actions.simulate')}>
                        <IconButton onClick={() => setSimulateFor(rule)}><ScienceRoundedIcon /></IconButton>
                      </Tooltip>
                      <Tooltip title={t('actions.runHistory')}>
                        <IconButton onClick={() => setHistoryFor(rule)}><HistoryRoundedIcon /></IconButton>
                      </Tooltip>
                      {rule.isProtected ? (
                        <Chip size="small" label={t('chips.alwaysOn')} variant="outlined" />
                      ) : (
                        <TextField
                          select size="small" value={rule.status} sx={{ minWidth: 110 }}
                          onChange={(e) => changeStatus(rule, e.target.value as RuleStatus)}
                        >
                          {STATUS_OPTIONS.map((s) => <MenuItem key={s} value={s}>{t(`status.${s}`)}</MenuItem>)}
                        </TextField>
                      )}
                      <Tooltip title={rule.isProtected ? t('tooltips.cannotDelete') : t('tooltips.delete')}>
                        <span>
                          <IconButton onClick={() => remove(rule)} disabled={rule.isProtected}>
                            <DeleteOutlineRoundedIcon />
                          </IconButton>
                        </span>
                      </Tooltip>
                    </Stack>
                  </Stack>
                </CardContent>
              </Card>
            ))}
          </Stack>
        )}
      </Box>

      <CreateDialog draft={draft} devices={devices} onChange={setDraft} onClose={() => setDraft(null)} onSave={save} />
      <HistoryDrawer target={historyFor} onClose={() => setHistoryFor(null)} />
      <SimulateDialog rule={simulateFor} onClose={() => setSimulateFor(null)} />
    </Container>
  );
}

function CreateDialog({
  draft, devices, onChange, onClose, onSave,
}: {
  draft: DraftState | null;
  devices: CapabilityDevice[];
  onChange: (d: DraftState) => void;
  onClose: () => void;
  onSave: () => void;
}) {
  const { t } = useTranslation('automations');
  const caps = (deviceId: string) => devices.find((d) => d.id === deviceId)?.capabilities ?? [];
  const valid = draft && draft.name.trim() && draft.trigDevice && draft.trigCap && draft.actDevice && draft.actCap;

  return (
    <Dialog open={draft !== null} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('dialog.createTitle')}</DialogTitle>
      <DialogContent>
        {draft && (
          <Stack spacing={2.5} mt={1}>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
              <TextField label={t('dialog.name')} value={draft.name} autoFocus required fullWidth
                onChange={(e) => onChange({ ...draft, name: e.target.value })} />
              <TextField select label={t('dialog.status')} value={draft.status} sx={{ minWidth: 140 }}
                helperText={draft.status === 'Shadow' ? t('dialog.shadowHelper')
                  : draft.status === 'BoundedActive' ? t('dialog.boundedHelper') : ' '}
                onChange={(e) => onChange({ ...draft, status: e.target.value as RuleStatus })}>
                <MenuItem value="Active">{t('status.Active')}</MenuItem>
                <MenuItem value="BoundedActive">{t('status.BoundedActive')}</MenuItem>
                <MenuItem value="Shadow">{t('status.Shadow')}</MenuItem>
              </TextField>
            </Stack>

            <Box>
              <Typography variant="overline" color="text.secondary">{t('dialog.whenTrigger')}</Typography>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} mt={0.5}>
                <TextField select label={t('dialog.device')} value={draft.trigDevice} fullWidth
                  onChange={(e) => onChange({ ...draft, trigDevice: e.target.value, trigCap: '' })}>
                  {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
                </TextField>
                <TextField select label={t('dialog.capability')} value={draft.trigCap} fullWidth disabled={!draft.trigDevice}
                  onChange={(e) => onChange({ ...draft, trigCap: e.target.value })}>
                  {caps(draft.trigDevice).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
                </TextField>
              </Stack>
              <Stack direction="row" spacing={1.5} mt={1.5}>
                <TextField select label={t('dialog.operator')} value={draft.trigOp} sx={{ width: 140 }}
                  onChange={(e) => onChange({ ...draft, trigOp: e.target.value })}>
                  {OPERATORS.map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}
                </TextField>
                <TextField label={t('dialog.value')} value={draft.trigValue} fullWidth disabled={draft.trigOp === 'changed'}
                  helperText={t('dialog.valueHelper')}
                  onChange={(e) => onChange({ ...draft, trigValue: e.target.value })} />
              </Stack>
              <FormControlLabel sx={{ mt: 0.5 }}
                control={<Checkbox checked={draft.onlyDark} onChange={(e) => onChange({ ...draft, onlyDark: e.target.checked })} />}
                label={t('dialog.onlyDark')} />
            </Box>

            <Divider />

            <Box>
              <Typography variant="overline" color="text.secondary">{t('dialog.thenAction')}</Typography>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} mt={0.5}>
                <TextField select label={t('dialog.device')} value={draft.actDevice} fullWidth
                  onChange={(e) => onChange({ ...draft, actDevice: e.target.value, actCap: '' })}>
                  {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
                </TextField>
                <TextField select label={t('dialog.capability')} value={draft.actCap} fullWidth disabled={!draft.actDevice}
                  onChange={(e) => onChange({ ...draft, actCap: e.target.value })}>
                  {caps(draft.actDevice).filter((c) => c.writable).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
                </TextField>
              </Stack>
              <Stack direction="row" spacing={1.5} mt={1.5}>
                <TextField label={t('dialog.value')} value={draft.actValue} fullWidth
                  onChange={(e) => onChange({ ...draft, actValue: e.target.value })} />
                <TextField type="number" label={t('dialog.autoOff')} value={draft.autoOffSeconds} sx={{ width: 180 }}
                  helperText={t('dialog.autoOffHelper')}
                  onChange={(e) => onChange({ ...draft, autoOffSeconds: Number(e.target.value) || 0 })} />
              </Stack>
            </Box>
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('actions.cancel')}</Button>
        <Button variant="contained" onClick={onSave} disabled={!valid}>{t('actions.create')}</Button>
      </DialogActions>
    </Dialog>
  );
}

const DAYS_OPTIONS = [1, 7, 30];

/** Dry-run a rule over history (roadmap Epic 1F) — shows when it would have fired, without acting. */
function SimulateDialog({ rule, onClose }: { rule: AutomationRule | null; onClose: () => void }) {
  const { t } = useTranslation('automations');
  const [days, setDays] = useState(7);
  const [result, setResult] = useState<ReplayResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);

  const run = useCallback(async (r: AutomationRule, d: number) => {
    setLoading(true); setError(false); setResult(null);
    try { setResult(await replayApi.run(r, d)); }
    catch { setError(true); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => {
    if (rule) run(rule, days);
    else { setResult(null); setError(false); }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rule]);

  return (
    <Dialog open={rule !== null} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('simulate.title', { name: rule?.name })}</DialogTitle>
      <DialogContent>
        <Stack direction="row" spacing={1} alignItems="center" mb={2} mt={1}>
          <TextField select size="small" label={t('simulate.window')} value={days} sx={{ width: 160 }}
            onChange={(e) => { const d = Number(e.target.value); setDays(d); if (rule) run(rule, d); }}>
            {DAYS_OPTIONS.map((d) => <MenuItem key={d} value={d}>{t('simulate.lastDays', { count: d })}</MenuItem>)}
          </TextField>
          {result && (
            <Typography variant="body2" color="text.secondary">
              {t('simulate.summary', { fires: result.fires, matches: result.hits.length, scanned: result.eventsScanned })}
            </Typography>
          )}
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2 }} />}
        {error && <Alert severity="warning">{t('simulate.failed')}</Alert>}

        {result?.notes.map((n, i) => (
          <Alert key={i} severity="info" sx={{ mb: 1 }}>{n}</Alert>
        ))}

        {result && !loading && result.hits.length === 0 && !error && (
          <Typography variant="body2" color="text.secondary">
            {t('simulate.noTrigger')}
          </Typography>
        )}

        <Stack spacing={1} mt={1}>
          {result?.hits.map((h, i) => (
            <Stack key={`${h.timestamp}-${i}`} direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Chip size="small" variant="outlined" color={h.conditionsMet ? 'success' : 'default'}
                label={h.conditionsMet ? t('simulate.wouldFire') : t('simulate.triggerOnly')} />
              <Typography variant="body2" color="text.secondary">{h.triggerSummary}</Typography>
              <Box flex={1} />
              <Typography variant="caption" color="text.secondary">{fmtDateTime(h.timestamp)}</Typography>
            </Stack>
          ))}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('actions.close')}</Button>
      </DialogActions>
    </Dialog>
  );
}

function HistoryDrawer({ target, onClose }: { target: AutomationRule | 'all' | null; onClose: () => void }) {
  const { t } = useTranslation('automations');
  const [rows, setRows] = useState<AutoHistoryEntry[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (target === null) return;
    let cancelled = false;
    setLoading(true);
    automationsApi
      .getHistory(target === 'all' ? undefined : target.id, 100)
      .then((r) => { if (!cancelled) setRows(r); })
      .catch(() => { if (!cancelled) setRows([]); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [target]);

  const title = target === 'all' ? t('history.allTitle') : target?.name ?? '';

  return (
    <Drawer anchor="right" open={target !== null} onClose={onClose}
      PaperProps={{ sx: { width: { xs: '100%', sm: 420 }, maxWidth: '100%' } }}>
      <Box p={2.5}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={2}>
          <Typography variant="h6" fontWeight={700}>{title}</Typography>
          <IconButton onClick={onClose} edge="end"><CloseRoundedIcon /></IconButton>
        </Stack>
        {loading && <LinearProgress sx={{ mb: 2 }} />}
        {!loading && rows.length === 0 && (
          <Typography variant="body2" color="text.secondary">{t('history.empty')}</Typography>
        )}
        <Stack spacing={1.25}>
          {rows.map((e, i) => (
            <Box key={`${e.timestamp}-${i}`}>
              <Stack direction="row" alignItems="center" spacing={1} flexWrap="wrap" useFlexGap>
                <Chip size="small" variant="outlined"
                  color={e.success && e.conditionsMet ? 'success' : e.conditionsMet ? 'error' : 'default'}
                  label={e.conditionsMet ? (e.success ? t('history.ran', { count: e.actionsExecuted }) : t('history.failed')) : t('history.skipped')} />
                {target === 'all' && <Typography variant="body2" fontWeight={600}>{e.ruleName}</Typography>}
                <Box flex={1} />
                <Typography variant="caption" color="text.secondary">
                  {fmtDateTime(e.timestamp)}
                </Typography>
              </Stack>
              <Typography variant="caption" color="text.secondary">{e.triggerSummary}{e.detail ? ` — ${e.detail}` : ''}</Typography>
            </Box>
          ))}
        </Stack>
      </Box>
    </Drawer>
  );
}
