// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import { fmtDateTime } from '../i18n/format';
import {
  Container, Box, Typography, Stack, Button, IconButton, LinearProgress, Alert,
  Card, CardContent, Tooltip, Chip, Dialog, DialogTitle, DialogContent,
  DialogActions, TextField, MenuItem, Divider, Drawer, Paper,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import ScienceRoundedIcon from '@mui/icons-material/ScienceRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import HealthAndSafetyRoundedIcon from '@mui/icons-material/HealthAndSafetyRounded';
import {
  automationsApi, AutomationRule, AutoHistoryEntry, RuleTrigger, RuleCondition, RuleAction, NewRule, RuleStatus,
} from '../api/automations';
import { replayApi, ReplayResult } from '../api/replay';
import { operatorSummary } from '../i18n/optionLabels';
import { capabilityDevicesApi, CapabilityDevice } from '../api/capabilityDevices';
import { scenesApi, Scene } from '../api/scenes';
import {
  RuleDraft, emptyDraft, draftFromRule, isDraftValid, newTrigger, newCondition, newAction,
  SafetyTemplate, SAFETY_TEMPLATES, recommendedTemplateIds, buildDraftFromTemplate,
} from '../data/safetyTemplates';
import { TriggerEditor, ConditionEditor, ActionEditor } from '../components/automations/RuleEditors';
import { fmt } from '../components/automations/ruleValues';
import { useFocusParam, scrollIntoViewRef } from '../hooks/useFocusParam';

// User-selectable lifecycle (Proposed/Approved are reserved for ML proposals, Epic 1F/Phase 2).
// BoundedActive (Epic 1F) = active but rate-limited — the stage between Shadow and full Active.
const STATUS_OPTIONS: RuleStatus[] = ['Active', 'BoundedActive', 'Shadow', 'Disabled'];
const statusColor = (s: RuleStatus): 'success' | 'info' | 'warning' | 'default' =>
  s === 'Active' ? 'success' : s === 'BoundedActive' ? 'warning' : s === 'Shadow' ? 'info' : 'default';

/** A rule with the operator "changed" carries no comparison value — normalize it away before sending. */
const normalizeTrigger = (t: RuleTrigger): RuleTrigger =>
  t.type === 'DeviceState' && t.operator === 'changed' ? { ...t, value: null } : t;

export default function Automations() {
  const { t } = useTranslation('automations');
  const focusId = useFocusParam();
  const [rules, setRules] = useState<AutomationRule[]>([]);
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [scenes, setScenes] = useState<Scene[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [draft, setDraft] = useState<RuleDraft | null>(null);
  const [historyFor, setHistoryFor] = useState<AutomationRule | 'all' | null>(null);
  const [simulateFor, setSimulateFor] = useState<AutomationRule | null>(null);
  const [templatePicker, setTemplatePicker] = useState(false);

  const deviceName = useCallback(
    (id?: string | null) => devices.find((d) => d.id === id)?.name ?? id ?? '—',
    [devices],
  );

  const load = useCallback(async () => {
    setError(null);
    try {
      const [r, d, s] = await Promise.all([
        automationsApi.getRules(),
        capabilityDevicesApi.getDevices(),
        scenesApi.getScenes().catch(() => [] as Scene[]),
      ]);
      setRules(r);
      setDevices(d);
      setScenes(s);
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
    if (t.type === 'DeviceState') return `${t.capabilityId ?? i18n.t('automations:trigger.any')} ${operatorSummary(i18n.t, t.operator)} ${fmt(t.value)} · ${deviceName(t.deviceId)}`;
    if (t.type === 'Time') return i18n.t('automations:trigger.schedule', { cron: t.cron ?? '' });
    return `${t.sun ?? i18n.t('automations:trigger.sun')}${t.offsetMinutes ? ` ${t.offsetMinutes > 0 ? '+' : ''}${t.offsetMinutes}m` : ''}`;
  }, [deviceName]);

  const conditionText = (c: RuleCondition): string => {
    if (c.type === 'Sun') return c.dark === false ? i18n.t('automations:condition.whileLight') : i18n.t('automations:condition.whileDark');
    if (c.type === 'TimeOfDay') return `${c.fromTime}–${c.toTime}`;
    if (c.type === 'Mode') return i18n.t('automations:condition.mode', { mode: c.mode });
    return `${c.capabilityId} ${operatorSummary(i18n.t, c.operator)} ${fmt(c.value)}`;
  };

  const actionText = useCallback((a: RuleAction): string => {
    if (a.type === 'Command') return `${i18n.t('automations:action.set', { assignments: Object.entries(a.set ?? {}).map(([k, v]) => `${k}=${fmt(v)}`).join(', ') })} · ${deviceName(a.deviceId)}`;
    if (a.type === 'Delay') return i18n.t('automations:action.wait', { seconds: a.delaySeconds });
    if (a.type === 'Scene') return i18n.t('automations:action.scene', { name: scenes.find((s) => s.id === a.sceneId)?.name ?? a.sceneId ?? '—' });
    return i18n.t('automations:action.notify', { message: a.message ?? '' });
  }, [deviceName, scenes]);

  // Adopt a safety template (roadmap: safety rules as configurable templates, not a hardcoded floor):
  // pre-fill the builder from the template, layer localized text on top, then let the user bind it to
  // their own sensor/actuator before saving. Nothing runs until they hit Create.
  const adoptTemplate = (tpl: SafetyTemplate) => {
    const d = buildDraftFromTemplate(tpl, devices);
    d.name = t(`templates.items.${tpl.id}.title`);
    if (tpl.actionKind === 'Notify' && d.actions[0]?.type === 'Notify') {
      d.actions[0] = { ...d.actions[0], message: t(`templates.items.${tpl.id}.message`) };
    }
    setTemplatePicker(false);
    setDraft(d);
  };

  const save = async () => {
    if (!draft || !isDraftValid(draft)) return;

    const payload: NewRule = {
      name: draft.name.trim(), description: null, status: draft.status,
      triggers: draft.triggers.map(normalizeTrigger),
      conditions: draft.conditions,
      actions: draft.actions,
    };
    try {
      if (draft.id) {
        const existing = rules.find((r) => r.id === draft.id);
        if (existing) await automationsApi.updateRule(draft.id, { ...existing, ...payload });
      } else {
        await automationsApi.createRule(payload);
      }
      setDraft(null);
      await load();
    } catch {
      setError(draft.id ? t('errors.update') : t('errors.create'));
    }
  };

  const sorted = useMemo(
    () => [...rules].sort((a, b) => a.name.localeCompare(b.name)),
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
            <Button startIcon={<AutoAwesomeRoundedIcon />} onClick={() => setTemplatePicker(true)}>{t('actions.fromTemplate')}</Button>
            <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={() => setDraft(emptyDraft())}>
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
              <Card key={rule.id} variant="outlined" ref={scrollIntoViewRef(focusId === rule.id)}
                sx={focusId === rule.id ? { borderColor: 'primary.main', boxShadow: 2 } : undefined}>
                <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                  <Stack direction="row" alignItems="flex-start" spacing={2}>
                    <Box flex={1} minWidth={0}>
                      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.5}>
                        <Typography fontWeight={700}>{rule.name}</Typography>
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
                      <Tooltip title={t('actions.edit')}>
                        <IconButton onClick={() => setDraft(draftFromRule(rule))}><EditRoundedIcon /></IconButton>
                      </Tooltip>
                      <Tooltip title={t('actions.simulate')}>
                        <IconButton onClick={() => setSimulateFor(rule)}><ScienceRoundedIcon /></IconButton>
                      </Tooltip>
                      <Tooltip title={t('actions.runHistory')}>
                        <IconButton onClick={() => setHistoryFor(rule)}><HistoryRoundedIcon /></IconButton>
                      </Tooltip>
                      <TextField
                        select size="small" value={rule.status} sx={{ minWidth: 110 }}
                        onChange={(e) => changeStatus(rule, e.target.value as RuleStatus)}
                      >
                        {STATUS_OPTIONS.map((s) => <MenuItem key={s} value={s}>{t(`status.${s}`)}</MenuItem>)}
                      </TextField>
                      <Tooltip title={t('tooltips.delete')}>
                        <IconButton onClick={() => remove(rule)}>
                          <DeleteOutlineRoundedIcon />
                        </IconButton>
                      </Tooltip>
                    </Stack>
                  </Stack>
                </CardContent>
              </Card>
            ))}
          </Stack>
        )}
      </Box>

      <TemplateGallery
        open={templatePicker} devices={devices} rules={rules}
        onPick={adoptTemplate} onClose={() => setTemplatePicker(false)} />
      <RuleDialog draft={draft} devices={devices} scenes={scenes} onChange={setDraft} onClose={() => setDraft(null)} onSave={save} />
      <HistoryDrawer target={historyFor} onClose={() => setHistoryFor(null)} />
      <SimulateDialog rule={simulateFor} onClose={() => setSimulateFor(null)} />
    </Container>
  );
}

/**
 * Rule builder — create or edit an automation. Three list sections mirror the rule model: triggers (any
 * fires — OR), conditions (all must hold — AND, optional) and actions (run in order). Each row is an
 * independent editor with add/remove, so a rule can watch several triggers and do several things.
 */
function RuleDialog({
  draft, devices, scenes, onChange, onClose, onSave,
}: {
  draft: RuleDraft | null;
  devices: CapabilityDevice[];
  scenes: Scene[];
  onChange: (d: RuleDraft) => void;
  onClose: () => void;
  onSave: () => void;
}) {
  const { t } = useTranslation('automations');
  const valid = draft ? isDraftValid(draft) : false;
  const editing = !!draft?.id;

  return (
    <Dialog open={draft !== null} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{editing ? t('dialog.editTitle') : t('dialog.createTitle')}</DialogTitle>
      <DialogContent>
        {draft && <RuleDialogBody draft={draft} devices={devices} scenes={scenes} onChange={onChange} />}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('actions.cancel')}</Button>
        <Button variant="contained" onClick={onSave} disabled={!valid}>
          {editing ? t('actions.save') : t('actions.create')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function RuleDialogBody({
  draft, devices, scenes, onChange,
}: {
  draft: RuleDraft;
  devices: CapabilityDevice[];
  scenes: Scene[];
  onChange: (d: RuleDraft) => void;
}) {
  const { t } = useTranslation('automations');
  const caps = (id?: string | null) => devices.find((d) => d.id === id)?.capabilities ?? [];
  const setTriggers = (triggers: RuleTrigger[]) => onChange({ ...draft, triggers });
  const setConditions = (conditions: RuleCondition[]) => onChange({ ...draft, conditions });
  const setActions = (actions: RuleAction[]) => onChange({ ...draft, actions });

  return (
    <Stack spacing={2.5} mt={1}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
        <TextField label={t('dialog.name')} value={draft.name} autoFocus required fullWidth
          onChange={(e) => onChange({ ...draft, name: e.target.value })} />
        <TextField select label={t('dialog.status')} value={draft.status} sx={{ minWidth: 150 }}
          helperText={draft.status === 'Shadow' ? t('dialog.shadowHelper')
            : draft.status === 'BoundedActive' ? t('dialog.boundedHelper') : ' '}
          onChange={(e) => onChange({ ...draft, status: e.target.value as RuleStatus })}>
          <MenuItem value="Active">{t('status.Active')}</MenuItem>
          <MenuItem value="BoundedActive">{t('status.BoundedActive')}</MenuItem>
          <MenuItem value="Shadow">{t('status.Shadow')}</MenuItem>
        </TextField>
      </Stack>

      <Divider />

      <EditorSection title={t('sections.when')} hint={t('sections.whenHint')}
        addLabel={t('sections.addTrigger')} onAdd={() => setTriggers([...draft.triggers, newTrigger()])}>
        {draft.triggers.map((tr, i) => (
          <RuleRow key={i} connector={i > 0 ? t('sections.or') : undefined} deletable={draft.triggers.length > 1}
            onDelete={() => setTriggers(draft.triggers.filter((_, x) => x !== i))}>
            <TriggerEditor devices={devices} caps={caps} value={tr}
              onChange={(v) => setTriggers(draft.triggers.map((x, xi) => (xi === i ? v : x)))} />
          </RuleRow>
        ))}
      </EditorSection>

      <Divider />

      <EditorSection title={t('sections.if')} hint={t('sections.ifHint')}
        addLabel={t('sections.addCondition')} onAdd={() => setConditions([...draft.conditions, newCondition()])}>
        {draft.conditions.length === 0 && (
          <Typography variant="body2" color="text.secondary">{t('sections.noConditions')}</Typography>
        )}
        {draft.conditions.map((c, i) => (
          <RuleRow key={i} connector={i > 0 ? t('sections.and') : undefined} deletable
            onDelete={() => setConditions(draft.conditions.filter((_, x) => x !== i))}>
            <ConditionEditor devices={devices} caps={caps} value={c}
              onChange={(v) => setConditions(draft.conditions.map((x, xi) => (xi === i ? v : x)))} />
          </RuleRow>
        ))}
      </EditorSection>

      <Divider />

      <EditorSection title={t('sections.then')} hint={t('sections.thenHint')}
        addLabel={t('sections.addAction')} onAdd={() => setActions([...draft.actions, newAction()])}>
        {draft.actions.map((a, i) => (
          <RuleRow key={i} connector={i > 0 ? t('sections.andThen') : undefined} deletable={draft.actions.length > 1}
            onDelete={() => setActions(draft.actions.filter((_, x) => x !== i))}>
            <ActionEditor devices={devices} caps={caps} scenes={scenes} value={a}
              onChange={(v) => setActions(draft.actions.map((x, xi) => (xi === i ? v : x)))} />
          </RuleRow>
        ))}
      </EditorSection>
    </Stack>
  );
}

/** A titled builder section (When / If / Then) with a hint and an "add row" button. */
function EditorSection({
  title, hint, addLabel, onAdd, children,
}: {
  title: string; hint: string; addLabel: string; onAdd: () => void; children: ReactNode;
}) {
  return (
    <Box>
      <Typography variant="overline" color="text.secondary" display="block">{title}</Typography>
      <Typography variant="caption" color="text.secondary" display="block" sx={{ mb: 1 }}>{hint}</Typography>
      <Stack spacing={1}>{children}</Stack>
      <Button size="small" startIcon={<AddRoundedIcon />} onClick={onAdd} sx={{ mt: 1 }}>{addLabel}</Button>
    </Box>
  );
}

/** One editor row inside a section — a bordered card with an optional or/and/then connector and a delete button. */
function RuleRow({
  connector, deletable, onDelete, children,
}: {
  connector?: string; deletable: boolean; onDelete: () => void; children: ReactNode;
}) {
  return (
    <>
      {connector && (
        <Typography variant="overline" color="text.secondary" sx={{ display: 'block', textAlign: 'center', my: -0.5 }}>
          {connector}
        </Typography>
      )}
      <Paper variant="outlined" sx={{ p: 1.5, pr: 5, position: 'relative', borderRadius: 2 }}>
        {deletable && (
          <IconButton size="small" onClick={onDelete} sx={{ position: 'absolute', top: 6, right: 6 }}>
            <DeleteOutlineRoundedIcon fontSize="small" />
          </IconButton>
        )}
        {children}
      </Paper>
    </>
  );
}

/**
 * Safety-template gallery: curated protective rules the user can adopt. Templates whose trigger sensor
 * the home actually has (and which no rule watches yet) are flagged "recommended" and sorted first —
 * a gentle suggestion, never an auto-created rule. Picking one opens the builder pre-filled.
 */
function TemplateGallery({
  open, devices, rules, onPick, onClose,
}: {
  open: boolean;
  devices: CapabilityDevice[];
  rules: AutomationRule[];
  onPick: (tpl: SafetyTemplate) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation('automations');
  const recommended = useMemo(() => recommendedTemplateIds(SAFETY_TEMPLATES, devices, rules), [devices, rules]);
  const ordered = useMemo(
    () => [...SAFETY_TEMPLATES].sort((a, b) => Number(recommended.has(b.id)) - Number(recommended.has(a.id))),
    [recommended],
  );

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('templates.galleryTitle')}</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" mb={2}>{t('templates.gallerySubtitle')}</Typography>
        <Stack spacing={1.25}>
          {ordered.map((tpl) => (
            <Card key={tpl.id} variant="outlined">
              <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                <Stack direction="row" alignItems="center" spacing={1.5}>
                  <HealthAndSafetyRoundedIcon color="warning" />
                  <Box flex={1} minWidth={0}>
                    <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.25}>
                      <Typography fontWeight={700}>{t(`templates.items.${tpl.id}.title`)}</Typography>
                      {recommended.has(tpl.id) && (
                        <Chip size="small" color="success" variant="outlined" label={t('templates.recommended')} />
                      )}
                      <Chip size="small" variant="outlined"
                        label={tpl.actionKind === 'Notify' ? t('dialog.actionNotify') : t('dialog.actionCommand')} />
                    </Stack>
                    <Typography variant="body2" color="text.secondary">
                      {t(`templates.items.${tpl.id}.desc`)}
                    </Typography>
                  </Box>
                  <Button variant="outlined" size="small" onClick={() => onPick(tpl)}>{t('templates.use')}</Button>
                </Stack>
              </CardContent>
            </Card>
          ))}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('actions.close')}</Button>
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
