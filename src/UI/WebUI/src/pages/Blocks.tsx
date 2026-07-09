// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import {
  Container, Box, Typography, Stack, Button, IconButton, LinearProgress, Alert,
  Card, CardContent, Tooltip, Chip, Dialog, DialogTitle, DialogContent, DialogActions,
  TextField, MenuItem, Divider, ToggleButtonGroup, ToggleButton,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import ViewListRoundedIcon from '@mui/icons-material/ViewListRounded';
import PublishRoundedIcon from '@mui/icons-material/PublishRounded';
import CircleIcon from '@mui/icons-material/Circle';
import { blocksApi, BlockCatalogEntry, BlockStatus, ControlBlock, NewBlock, PortBinding } from '../api/blocks';
import { capabilityDevicesApi, CapabilityDevice } from '../api/capabilityDevices';
import { proposalsApi } from '../api/proposals';
import { fmtDateTime } from '../i18n/format';
import BlockGraph from '../components/blocks/BlockGraph';
import { toNewBlock } from '../components/blocks/blockGraphModel';

const stageName = (s: number) =>
  i18n.t(s >= 2 ? 'blocks:stageName.full' : s === 1 ? 'blocks:stageName.bounded' : 'blocks:stageName.shadow');

// Human-readable parameter label/description: prefer a per-type i18n string (blocks:param.<type>.<name>),
// fall back to a shared governor entry (param._common), then to the catalog's raw name/English description.
// This is what turns the bare "coolSetpoint / hysteresis" keys into localized fields (issue #2).
const paramLabel = (typeId: string, p: { name: string; unit?: string | null }): string => {
  const specific = `blocks:param.${typeId}.${p.name}.label`;
  const common = `blocks:param._common.${p.name}.label`;
  const base = i18n.exists(specific) ? i18n.t(specific) : i18n.exists(common) ? i18n.t(common) : p.name;
  return p.unit ? `${base} (${p.unit})` : base;
};
const paramDesc = (typeId: string, p: { name: string; description: string }): string => {
  const specific = `blocks:param.${typeId}.${p.name}.desc`;
  const common = `blocks:param._common.${p.name}.desc`;
  return i18n.exists(specific) ? i18n.t(specific) : i18n.exists(common) ? i18n.t(common) : p.description;
};

// Live health of a block, folded into a single chip (issue #3): is it actually ticking, idle, or errored?
type BlockHealth = { label: string; color: 'success' | 'warning' | 'error' | 'default'; hint: string };
const blockHealth = (block: ControlBlock, status: BlockStatus | undefined): BlockHealth => {
  if (!block.enabled) return { label: i18n.t('blocks:status.disabled'), color: 'default', hint: i18n.t('blocks:status.disabledHint') };
  if (!status) return { label: i18n.t('blocks:status.idle'), color: 'default', hint: i18n.t('blocks:status.unknownHint') };
  if (status.lastError) return { label: i18n.t('blocks:status.error'), color: 'error', hint: i18n.t('blocks:status.errorHint', { error: status.lastError }) };
  if (!status.lastTickAt) return { label: i18n.t('blocks:status.idle'), color: 'warning', hint: i18n.t('blocks:status.idleHint') };
  const ageMs = Date.now() - new Date(status.lastTickAt).getTime();
  if (ageMs > 60_000) return { label: i18n.t('blocks:status.stalled'), color: 'warning', hint: i18n.t('blocks:status.stalledHint') };
  return {
    label: i18n.t('blocks:status.running'), color: 'success',
    hint: i18n.t('blocks:status.runningHint', { when: fmtDateTime(status.lastTickAt), count: status.tickCount }),
  };
};

// The authority ladder phrased as trust in the house spirit (2B staging):
// it starts by watching, then acts carefully, then runs the loop on its own.
const stageHint = (s: number) =>
  i18n.t(s >= 2 ? 'blocks:stageHint.full' : s === 1 ? 'blocks:stageHint.bounded' : 'blocks:stageHint.shadow');

const fmt = (v: unknown): string => {
  if (v === null || v === undefined || v === '') return '—';
  if (typeof v === 'boolean') return v ? 'on' : 'off';
  if (typeof v === 'number') return String(Math.round(v * 100) / 100);
  return String(v);
};

interface BlockDraft {
  id?: string; // present ⇒ editing an existing block (issue #4)
  name: string;
  typeId: string;
  enabled: boolean;
  params: Record<string, number>;
  inputs: Record<string, PortBinding>;
  outputs: Record<string, PortBinding>;
}

export default function Blocks() {
  const { t } = useTranslation('blocks');
  const [blocks, setBlocks] = useState<ControlBlock[]>([]);
  const [catalog, setCatalog] = useState<BlockCatalogEntry[]>([]);
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [statuses, setStatuses] = useState<BlockStatus[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [draft, setDraft] = useState<BlockDraft | null>(null);
  const [view, setView] = useState<'list' | 'graph'>('list');

  const load = useCallback(async () => {
    setError(null);
    try {
      const [b, c, d] = await Promise.all([
        blocksApi.getBlocks(), blocksApi.getCatalog(), capabilityDevicesApi.getDevices(),
      ]);
      setBlocks(b);
      setCatalog(c);
      setDevices(d);
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  // Poll live output (blocks are virtual devices) + runtime health so state/status update on screen.
  useEffect(() => {
    const refresh = () => {
      capabilityDevicesApi.getDevices().then(setDevices).catch(() => undefined);
      blocksApi.getStatus().then(setStatuses).catch(() => undefined);
    };
    refresh();
    const timer = setInterval(refresh, 5000);
    return () => clearInterval(timer);
  }, []);

  const deviceById = useMemo(() => new Map(devices.map((d) => [d.id, d])), [devices]);
  const typeById = useMemo(() => new Map(catalog.map((t) => [t.typeId, t])), [catalog]);
  const statusById = useMemo(() => new Map(statuses.map((s) => [s.blockId, s])), [statuses]);

  const startCreate = (entry: BlockCatalogEntry) => {
    setDraft({
      name: '',
      typeId: entry.typeId,
      enabled: true,
      params: Object.fromEntries(entry.params.map((p) => [p.name, p.default])),
      inputs: Object.fromEntries(entry.inputs.map((p) => [p.name, { deviceId: '', capabilityId: '' }])),
      outputs: Object.fromEntries(entry.outputs.map((o) => [o.id, { deviceId: '', capabilityId: '' }])),
    });
  };

  // Open the same authoring dialog pre-filled with an existing block (issue #4). Every catalog port is
  // seeded so unbound ports still render; the block's saved bindings overwrite the ones it has.
  const startEdit = (b: ControlBlock) => {
    const entry = typeById.get(b.typeId);
    setDraft({
      id: b.id,
      name: b.name,
      typeId: b.typeId,
      enabled: b.enabled,
      params: { ...Object.fromEntries((entry?.params ?? []).map((p) => [p.name, p.default])), ...b.params },
      inputs: { ...Object.fromEntries((entry?.inputs ?? []).map((p) => [p.name, { deviceId: '', capabilityId: '' }])), ...b.inputs },
      outputs: { ...Object.fromEntries((entry?.outputs ?? []).map((o) => [o.id, { deviceId: '', capabilityId: '' }])), ...b.outputs },
    });
  };

  const save = async () => {
    if (!draft || !draft.name.trim()) return;
    // Drop unbound input/output ports (bindings are optional).
    const inputs = Object.fromEntries(
      Object.entries(draft.inputs).filter(([, b]) => b.deviceId && b.capabilityId),
    );
    const outputs = Object.fromEntries(
      Object.entries(draft.outputs).filter(([, b]) => b.deviceId && b.capabilityId),
    );
    const payload: NewBlock = {
      name: draft.name.trim(), typeId: draft.typeId, enabled: draft.enabled, params: draft.params, inputs, outputs,
    };
    try {
      if (draft.id) await blocksApi.updateBlock(draft.id, payload);
      else await blocksApi.createBlock(payload);
      setDraft(null);
      await load();
    } catch {
      setError(t(draft.id ? 'errors.update' : 'errors.create'));
    }
  };

  // Persist wiring/layout edited on the graph canvas (Epic 1E), then reload once so dirty state clears.
  const saveGraph = useCallback(async (changed: ControlBlock[]) => {
    setError(null);
    try {
      for (const b of changed) await blocksApi.updateBlock(b.id, toNewBlock(b));
      await load();
    } catch {
      setError(t('errors.update'));
    }
  }, [load, t]);

  const remove = async (b: ControlBlock) => {
    if (!window.confirm(t('confirmDelete', { name: b.name }))) return;
    try { await blocksApi.deleteBlock(b.id); await load(); }
    catch { setError(t('errors.delete')); }
  };

  // Promotion goes through the approval queue (Epic 2C), not a direct stage edit — a human approves the
  // Shadow → Bounded → Full step after reading the scorecard.
  const proposePromotion = async (b: ControlBlock) => {
    const current = Math.round(b.params.stage ?? 0);
    const next = Math.min(2, current + 1);
    setError(null); setInfo(null);
    try {
      await proposalsApi.create({
        kind: 'BlockPromotion',
        title: t('promote.title', { name: b.name, from: stageName(current), to: stageName(next) }),
        blockId: b.id,
        fromStage: current,
        toStage: next,
        source: 'user',
        rationale: t('promote.rationale'),
      });
      setInfo(t('promote.queued', { name: b.name }));
    } catch {
      setError(t('errors.promote'));
    }
  };

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={1}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t('subtitle')}
            </Typography>
          </Box>
          <ToggleButtonGroup size="small" exclusive value={view} onChange={(_, v) => v && setView(v)}>
            <ToggleButton value="list"><ViewListRoundedIcon fontSize="small" sx={{ mr: 0.5 }} />{t('view.list')}</ToggleButton>
            <ToggleButton value="graph"><AccountTreeRoundedIcon fontSize="small" sx={{ mr: 0.5 }} />{t('view.graph')}</ToggleButton>
          </ToggleButtonGroup>
        </Stack>

        {/* Catalog — one "New" per built-in type (typed authoring, roadmap Epic 1H). */}
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mb={3}>
          {catalog.map((t) => (
            <Tooltip key={t.typeId} title={t.description}>
              <Button size="small" variant="outlined" startIcon={<AddRoundedIcon />} onClick={() => startCreate(t)}>
                {t.title}
              </Button>
            </Tooltip>
          ))}
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
        {info && <Alert severity="info" sx={{ mb: 2 }} onClose={() => setInfo(null)}>{info}</Alert>}

        {view === 'graph' ? (
          <BlockGraph blocks={blocks} devices={devices} catalog={catalog} onSave={saveGraph} />
        ) : blocks.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <AccountTreeRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">
              {t('empty')}
            </Typography>
          </Box>
        ) : (
          <Stack spacing={1.5}>
            {blocks.map((b) => {
              const type = typeById.get(b.typeId);
              const vdev = deviceById.get(b.deviceId);
              // ML governor blocks carry a `stage` param; below Full they can be promoted via the queue.
              const isGovernor = type?.params.some((p) => p.name === 'stage') ?? false;
              const stage = Math.round(b.params.stage ?? 0);
              const health = blockHealth(b, statusById.get(b.id));
              return (
                <Card key={b.id} variant="outlined">
                  <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                    <Stack direction="row" alignItems="flex-start" spacing={2}>
                      <Box flex={1} minWidth={0}>
                        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.5}>
                          <Typography fontWeight={700}>{b.name}</Typography>
                          <Chip size="small" variant="outlined" label={type?.title ?? b.typeId} />
                          <Tooltip title={health.hint}>
                            <Chip size="small" variant="outlined" color={health.color}
                              icon={<CircleIcon sx={{ fontSize: '0.7rem !important' }} />} label={health.label} />
                          </Tooltip>
                          {isGovernor && (
                            <Tooltip title={stageHint(stage)}>
                              <Chip size="small" variant="outlined" color={stage === 0 ? 'default' : 'primary'}
                                label={t('chip.stage', { stage: stageName(stage) })} />
                            </Tooltip>
                          )}
                        </Stack>

                        {Object.keys(b.inputs).length > 0 && (
                          <Typography variant="body2" color="text.secondary">
                            <b>{t('labels.inputs')}</b>{' '}
                            {Object.entries(b.inputs).map(([port, bind]) =>
                              `${port} ← ${deviceById.get(bind.deviceId)?.name ?? bind.deviceId}.${bind.capabilityId}`).join(' · ')}
                          </Typography>
                        )}

                        <Typography variant="body2" color="text.secondary">
                          <b>{t('labels.output')}</b>{' '}
                          {vdev && vdev.state && Object.keys(vdev.state).length > 0
                            ? Object.entries(vdev.state).map(([k, v]) => `${k}=${fmt(v)}`).join(' · ')
                            : <em>{t('labels.noSamples')}</em>}
                        </Typography>
                        {Object.keys(b.outputs).length > 0 && (
                          <Typography variant="body2" color="text.secondary">
                            <b>{t('labels.drives')}</b>{' '}
                            {Object.entries(b.outputs).map(([cap, bind]) =>
                              `${cap} → ${deviceById.get(bind.deviceId)?.name ?? bind.deviceId}.${bind.capabilityId}`).join(' · ')}
                          </Typography>
                        )}
                      </Box>
                      <Stack direction="row" spacing={0.5} alignItems="center" sx={{ flexShrink: 0 }}>
                        {isGovernor && stage < 2 && (
                          <Tooltip title={t('promote.tooltip')}>
                            <Button size="small" variant="outlined" startIcon={<PublishRoundedIcon />}
                              onClick={() => proposePromotion(b)}>
                              {t('promote.button')}
                            </Button>
                          </Tooltip>
                        )}
                        <Tooltip title={t('actions.edit')}>
                          <IconButton onClick={() => startEdit(b)}><EditOutlinedIcon /></IconButton>
                        </Tooltip>
                        <Tooltip title={t('actions.delete')}>
                          <IconButton onClick={() => remove(b)}><DeleteOutlineRoundedIcon /></IconButton>
                        </Tooltip>
                      </Stack>
                    </Stack>
                  </CardContent>
                </Card>
              );
            })}
          </Stack>
        )}
      </Box>

      <CreateDialog
        draft={draft} catalog={typeById} devices={devices}
        onChange={setDraft} onClose={() => setDraft(null)} onSave={save}
      />
    </Container>
  );
}

function CreateDialog({
  draft, catalog, devices, onChange, onClose, onSave,
}: {
  draft: BlockDraft | null;
  catalog: Map<string, BlockCatalogEntry>;
  devices: CapabilityDevice[];
  onChange: (d: BlockDraft) => void;
  onClose: () => void;
  onSave: () => void;
}) {
  const { t } = useTranslation('blocks');
  const type = draft ? catalog.get(draft.typeId) : undefined;
  const capsOf = (deviceId: string) => devices.find((d) => d.id === deviceId)?.capabilities ?? [];
  const isEdit = !!draft?.id;

  return (
    <Dialog open={draft !== null} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>
        {t(isEdit ? 'dialog.editTitle' : 'dialog.title', { type: type?.title ?? t('dialog.blockFallback') })}
      </DialogTitle>
      <DialogContent>
        {draft && type && (
          <Stack spacing={2.5} mt={1}>
            <Typography variant="caption" color="text.secondary">{type.description}</Typography>
            <TextField label={t('dialog.name')} value={draft.name} autoFocus required fullWidth
              onChange={(e) => onChange({ ...draft, name: e.target.value })} />

            {type.inputs.length > 0 && (
              <Box>
                <Typography variant="overline" color="text.secondary">{t('dialog.inputsSection')}</Typography>
                {type.inputs.map((port) => {
                  const bind = draft.inputs[port.name] ?? { deviceId: '', capabilityId: '' };
                  return (
                    <Stack key={port.name} direction={{ xs: 'column', sm: 'row' }} spacing={1.5} mt={1}>
                      <TextField select label={t('dialog.deviceSuffix', { port: port.name })} value={bind.deviceId} fullWidth
                        helperText={port.description}
                        onChange={(e) => onChange({
                          ...draft,
                          inputs: { ...draft.inputs, [port.name]: { deviceId: e.target.value, capabilityId: '' } },
                        })}>
                        <MenuItem value=""><em>{t('dialog.none')}</em></MenuItem>
                        {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
                      </TextField>
                      <TextField select label={t('dialog.capability')} value={bind.capabilityId} fullWidth disabled={!bind.deviceId}
                        onChange={(e) => onChange({
                          ...draft,
                          inputs: { ...draft.inputs, [port.name]: { ...bind, capabilityId: e.target.value } },
                        })}>
                        {capsOf(bind.deviceId).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
                      </TextField>
                    </Stack>
                  );
                })}
              </Box>
            )}

            {type.outputs.length > 0 && (
              <Box>
                <Typography variant="overline" color="text.secondary">{t('dialog.outputsSection')}</Typography>
                {type.outputs.map((out) => {
                  const bind = draft.outputs[out.id] ?? { deviceId: '', capabilityId: '' };
                  return (
                    <Stack key={out.id} direction={{ xs: 'column', sm: 'row' }} spacing={1.5} mt={1}>
                      <TextField select label={t('dialog.outputDeviceSuffix', { output: out.id })} value={bind.deviceId} fullWidth
                        helperText={t('dialog.outputHelper')}
                        onChange={(e) => onChange({
                          ...draft,
                          outputs: { ...draft.outputs, [out.id]: { deviceId: e.target.value, capabilityId: '' } },
                        })}>
                        <MenuItem value=""><em>{t('dialog.none')}</em></MenuItem>
                        {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
                      </TextField>
                      <TextField select label={t('dialog.capability')} value={bind.capabilityId} fullWidth disabled={!bind.deviceId}
                        onChange={(e) => onChange({
                          ...draft,
                          outputs: { ...draft.outputs, [out.id]: { ...bind, capabilityId: e.target.value } },
                        })}>
                        {capsOf(bind.deviceId).filter((c) => c.writable).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
                      </TextField>
                    </Stack>
                  );
                })}
              </Box>
            )}

            {type.params.length > 0 && (
              <Box>
                <Typography variant="overline" color="text.secondary">{t('dialog.parametersSection')}</Typography>
                <Stack spacing={1.5} mt={1}>
                  {type.params.map((p) => (
                    p.name === 'stage' ? (
                      // ML authority stage (Epic 2B): a friendly selector over the numeric 0/1/2 param.
                      // Directly editable (Epic 2P decision №1): saving IS the human's explicit approval;
                      // the Promote button (approval queue, 2C) remains the system-initiated path.
                      <TextField
                        key={p.name} select label={t('dialog.authorityStage')}
                        value={draft.params[p.name] ?? p.default}
                        helperText={isEdit ? t('dialog.stageDirect') : paramDesc(draft.typeId, p)}
                        onChange={(e) => onChange({
                          ...draft, params: { ...draft.params, [p.name]: Number(e.target.value) },
                        })}
                      >
                        <MenuItem value={0}>{t('stageOption.shadow')}</MenuItem>
                        <MenuItem value={1}>{t('stageOption.bounded')}</MenuItem>
                        <MenuItem value={2}>{t('stageOption.full')}</MenuItem>
                      </TextField>
                    ) : p.name === 'mode' ? (
                      // Thermostat mode (numeric 0/1/2) shown as a readable selector instead of a bare number.
                      <TextField
                        key={p.name} select label={paramLabel(draft.typeId, p)}
                        value={draft.params[p.name] ?? p.default}
                        helperText={paramDesc(draft.typeId, p)}
                        onChange={(e) => onChange({
                          ...draft, params: { ...draft.params, [p.name]: Number(e.target.value) },
                        })}
                      >
                        <MenuItem value={0}>{t('modeOption.heat')}</MenuItem>
                        <MenuItem value={1}>{t('modeOption.cool')}</MenuItem>
                        <MenuItem value={2}>{t('modeOption.both')}</MenuItem>
                      </TextField>
                    ) : (
                      <TextField
                        key={p.name} type="number" label={paramLabel(draft.typeId, p)}
                        value={draft.params[p.name] ?? p.default}
                        helperText={paramDesc(draft.typeId, p)}
                        onChange={(e) => onChange({
                          ...draft, params: { ...draft.params, [p.name]: Number(e.target.value) },
                        })}
                      />
                    )
                  ))}
                </Stack>
              </Box>
            )}

            <Divider />
            <Typography variant="caption" color="text.secondary">
              {t('dialog.outputsSummary', {
                list: type.outputs.map((o) => `${o.id}${o.writable ? ` (${t('dialog.writable')})` : ''}`).join(', '),
              })}
            </Typography>
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('actions.cancel')}</Button>
        <Button variant="contained" onClick={onSave} disabled={!draft?.name.trim()}>
          {t(isEdit ? 'actions.save' : 'actions.create')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
