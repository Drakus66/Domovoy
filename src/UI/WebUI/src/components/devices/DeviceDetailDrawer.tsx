// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import {
  Box, Drawer, Stack, Typography, IconButton, Chip, Button, Tab, Tabs,
  TextField, MenuItem, InputAdornment, Tooltip,
} from '@mui/material';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import RestartAltRoundedIcon from '@mui/icons-material/RestartAltRounded';
import CircleIcon from '@mui/icons-material/Circle';
import ArrowRightAltRoundedIcon from '@mui/icons-material/ArrowRightAltRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import {
  CapabilityDevice, capabilityDevicesApi, isUnassignedZone, DEVICE_ARCHETYPES_FALLBACK, effectiveArchetype,
} from '../../api/capabilityDevices';
import type { Zone } from '../../api/zones';
import { historyApi, EventLogEntry } from '../../api/history';
import { automationsApi } from '../../api/automations';
import { blocksApi, BlockCatalogEntry, ControlBlock, PortBinding } from '../../api/blocks';
import { mlApi, MlTask } from '../../api/ml';
import { applicableTypesFor } from '../ml/mlHub';
import MlApplyWizard from '../ml/MlApplyWizard';
import CapabilityControl, { type CommandFn } from './CapabilityControls';
import { describeDevice } from './deviceVisuals';
import { deviceLabel, autoDeviceName, isCrypticName } from './deviceNaming';
import EnergyProfileEditor from './EnergyProfileEditor';
import LoadSheddingProfileEditor from './LoadSheddingProfileEditor';
import { fmtDateTime } from '../../i18n/format';
import TelemetryChart from '../charts/TelemetryChart';
import TriggerChip from '../common/TriggerChip';
import { deviceArchetypes } from '../../store/liveData';

const fmtValue = (v: unknown): string => {
  if (v === null || v === undefined || v === '') return '—';
  if (typeof v === 'boolean') return i18n.t(v ? 'devices:status.on' : 'devices:status.off');
  return String(v);
};

export type AssignZoneFn = (deviceId: string, zoneId: string | null) => void;
export type SetArchetypeFn = (deviceId: string, archetype: string | null) => void;
export type SetAliasFn = (deviceId: string, alias: string | null) => void;

/** Sliding panel with the full per-capability control surface for one device. */
export default function DeviceDetailDrawer({
  device, zones, open, onClose, onCommand, onAssignZone, onSetArchetype, onSetAlias,
}: {
  device: CapabilityDevice | null;
  zones: Zone[];
  open: boolean;
  onClose: () => void;
  onCommand: CommandFn;
  onAssignZone: AssignZoneFn;
  onSetArchetype: SetArchetypeFn;
  onSetAlias?: SetAliasFn;
}) {
  return (
    <Drawer
      anchor="right"
      open={open && !!device}
      onClose={onClose}
      PaperProps={{ sx: { width: { xs: '100%', sm: 460, md: 560 }, maxWidth: '100%' } }}
    >
      {device && (
        <DrawerBody
          device={device} zones={zones} onClose={onClose}
          onCommand={onCommand} onAssignZone={onAssignZone} onSetArchetype={onSetArchetype}
          onSetAlias={onSetAlias}
        />
      )}
    </Drawer>
  );
}

function DrawerBody({
  device, zones, onClose, onCommand, onAssignZone, onSetArchetype, onSetAlias,
}: {
  device: CapabilityDevice;
  zones: Zone[];
  onClose: () => void;
  onCommand: CommandFn;
  onAssignZone: AssignZoneFn;
  onSetArchetype: SetArchetypeFn;
  onSetAlias?: SetAliasFn;
}) {
  const { t } = useTranslation('devices');
  const { accent, Icon } = describeDevice(device);
  const offline = !device.isOnline;
  const currentZone = isUnassignedZone(device.zoneId) ? '' : device.zoneId;

  // Словарь типов приходит с сервера (общий ресурс, редкий опрос): локальная копия успела отстать
  // на три значения и не предлагала типов, которые система назначает сама.
  const archetypes = deviceArchetypes.use().data ?? DEVICE_ARCHETYPES_FALLBACK;

  // The panel is split into tabs (overview / history / settings) so the setup forms — alias, zone,
  // archetype, energy role, load-shedding profile — no longer sit above the live surface and squeeze
  // it into a sliver. Each tab owns the full scroll height; the tab resets when the device changes.
  const [tab, setTab] = useState(0);
  useEffect(() => { setTab(0); }, [device.id]);

  // Friendly name editor (Epic 3G): local draft committed on blur/Enter; empty clears the alias so the
  // device falls back to a type-derived label. The placeholder shows what that fallback would be.
  const [aliasDraft, setAliasDraft] = useState(device.alias ?? '');
  useEffect(() => { setAliasDraft(device.alias ?? ''); }, [device.id, device.alias]);
  const commitAlias = () => {
    const next = aliasDraft.trim();
    if (next === (device.alias ?? '').trim()) return;
    onSetAlias?.(device.id, next || null);
  };
  const clearAlias = () => { setAliasDraft(''); if (device.alias) onSetAlias?.(device.id, null); };

  // Recent history for this device (P0-5 event-log): shows who/what changed each capability.
  const [history, setHistory] = useState<EventLogEntry[]>([]);
  useEffect(() => {
    let cancelled = false;
    historyApi
      .getEvents({ deviceId: device.id, limit: 25 })
      .then((rows) => { if (!cancelled) setHistory(rows); })
      .catch(() => { if (!cancelled) setHistory([]); });
    return () => { cancelled = true; };
  }, [device.id]);

  // Rule id → name, so rule-caused changes can be explained with "why" (roadmap Epic 1F).
  const [ruleNames, setRuleNames] = useState<Record<string, string>>({});
  useEffect(() => {
    let cancelled = false;
    automationsApi.getRules()
      .then((rules) => { if (!cancelled) setRuleNames(Object.fromEntries(rules.map((r) => [r.id, r.name]))); })
      .catch(() => { if (!cancelled) setRuleNames({}); });
    return () => { cancelled = true; };
  }, []);
  // Concrete initiator behind a history row (attribution, Epic 2G tail): id from the structured
  // TriggerId (rule rows fall back to the legacy correlationId), name resolved best-effort from what
  // the drawer already has loaded (rules, ML-context blocks, this device itself).
  const triggerInfo = (e: EventLogEntry): { id: string | null; name: string | null } => {
    const id = e.triggerId || (e.triggerSource === 'rule' ? (e.ruleId || e.correlationId || null) : null);
    let name: string | null = null;
    if (id) {
      if (e.triggerSource === 'rule') name = ruleNames[id] ?? null;
      else if (e.triggerSource === 'block') name = mlCtx?.blocks.find((b) => b.id === id)?.name ?? null;
      else if (id === device.id) name = deviceLabel(device);
    }
    return { id, name };
  };

  // ML applicability (Epic 2P): if a governor block type can command one of this device's writable
  // capabilities, offer "connect a model" right here — the device-side entry into the apply wizard.
  const [mlCtx, setMlCtx] = useState<{
    catalog: BlockCatalogEntry[]; tasks: MlTask[]; blocks: ControlBlock[];
    devices: CapabilityDevice[]; types: BlockCatalogEntry[];
  } | null>(null);
  const [applyOpen, setApplyOpen] = useState(false);
  useEffect(() => {
    let cancelled = false;
    setMlCtx(null);
    blocksApi.getCatalog()
      .then(async (catalog) => {
        const types = applicableTypesFor(catalog, device);
        if (cancelled || types.length === 0) return;
        const [tasks, blocks, devs] = await Promise.all([
          mlApi.getTasks(), blocksApi.getBlocks(), capabilityDevicesApi.getDevices(),
        ]);
        if (!cancelled) setMlCtx({ catalog, tasks, blocks, devices: devs, types });
      })
      .catch(() => { if (!cancelled) setMlCtx(null); });
    return () => { cancelled = true; };
  }, [device.id]); // eslint-disable-line react-hooks/exhaustive-deps

  // Governor blocks already commanding this device (their bound output targets it).
  const governorTypeIds = new Set((mlCtx?.types ?? []).map((x) => x.typeId.toLowerCase()));
  const governedBy = (mlCtx?.blocks ?? []).filter((b) =>
    governorTypeIds.has(b.typeId.toLowerCase())
    && Object.values(b.outputs).some((o) => o.deviceId === device.id));

  // A control block appears here as a virtual device (Model "block/<type>"). Beyond its capabilities/state
  // (e.g. the setpoint), surface what it reads and what it drives so its role is visible (issue #5).
  const isBlock = device.model?.startsWith('block/') ?? false;
  const [wiring, setWiring] = useState<{ inputs: [string, PortBinding][]; outputs: [string, PortBinding][] } | null>(null);
  const [deviceNames, setDeviceNames] = useState<Record<string, string>>({});
  useEffect(() => {
    if (!isBlock) { setWiring(null); return; }
    let cancelled = false;
    Promise.all([blocksApi.getBlocks(), capabilityDevicesApi.getDevices()])
      .then(([blocks, devs]) => {
        if (cancelled) return;
        setDeviceNames(Object.fromEntries(devs.map((d) => [d.id, deviceLabel(d)])));
        const block = blocks.find((b) => b.deviceId === device.id);
        setWiring(block ? { inputs: Object.entries(block.inputs), outputs: Object.entries(block.outputs) } : null);
      })
      .catch(() => { if (!cancelled) setWiring(null); });
    return () => { cancelled = true; };
  }, [device.id, isBlock]);

  const controls = device.capabilities.filter((c) => c.writable || c.kind === 'Action');
  const sensors = device.capabilities.filter((c) => !c.writable && c.kind !== 'Action');
  // Numeric sensors get a 24h trend chart (roadmap Epic 1B).
  const numericSensors = sensors.filter((c) => c.kind === 'Number');

  const allOff = () => {
    const set: Record<string, unknown> = {};
    device.capabilities.forEach((c) => { if (c.id === 'on_off' && c.writable) set.on_off = false; });
    if (Object.keys(set).length) onCommand(device.id, set);
  };

  return (
    <Box sx={{ height: '100%', minHeight: 0, display: 'flex', flexDirection: 'column' }}>
      {/* Identity strip — stays put while the tab below scrolls. */}
      <Stack direction="row" alignItems="flex-start" spacing={1.5} sx={{ px: 2.5, pt: 2.5, pb: 1.5, flexShrink: 0 }}>
        <Box
          sx={{
            width: 48, height: 48, borderRadius: 2.5, flexShrink: 0,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            color: accent, bgcolor: `${accent}22`,
          }}
        >
          <Icon />
        </Box>
        <Box flex={1} minWidth={0}>
          <Typography variant="h6" fontWeight={700} sx={{ wordBreak: 'break-word' }}>
            {deviceLabel(device)}
          </Typography>
          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
            <Chip
              size="small"
              label={zones.find((z) => z.id === currentZone)?.name ?? t('unassigned')}
              variant="outlined"
            />
            <Chip size="small" color="info" variant="outlined"
              label={effectiveArchetype(device).replace(/_/g, ' ')} />
            <Stack direction="row" spacing={0.5} alignItems="center">
              <CircleIcon sx={{ fontSize: 9, color: offline ? 'text.disabled' : 'success.main' }} />
              <Typography variant="caption" color="text.secondary">
                {offline ? t('status.offline') : t('status.online')}
              </Typography>
            </Stack>
          </Stack>
        </Box>
        <IconButton onClick={onClose} aria-label={t('actions.close', { ns: 'common' })} edge="end"><CloseRoundedIcon /></IconButton>
      </Stack>

      <Tabs
        value={tab}
        onChange={(_, v: number) => setTab(v)}
        variant="fullWidth"
        sx={{ px: 1.5, borderBottom: 1, borderColor: 'divider', flexShrink: 0, minHeight: 40 }}
      >
        <Tab label={t('tabs.overview')} sx={{ minHeight: 40 }} />
        <Tab label={t('tabs.history')} sx={{ minHeight: 40 }} />
        <Tab label={t('tabs.settings')} sx={{ minHeight: 40 }} />
      </Tabs>

      {/* The one and only scroll surface: whichever tab is active gets the whole remaining height. */}
      <Box sx={{ flex: 1, minHeight: 0, overflowY: 'auto', px: 2.5, py: 2.5, '& > :last-child': { mb: 0 } }}>
        {tab === 0 && (
          <>
            {wiring && (wiring.inputs.length > 0 || wiring.outputs.length > 0) && (
              <Section title={t('blockIo.title')}>
                <Stack spacing={0.75}>
                  {wiring.inputs.map(([port, bind]) => (
                    <Typography key={`in-${port}`} variant="body2" color="text.secondary">
                      <b>{t('blockIo.reads')}</b>{' '}
                      {port} ← {deviceNames[bind.deviceId] ?? bind.deviceId}.{bind.capabilityId}
                    </Typography>
                  ))}
                  {wiring.outputs.map(([cap, bind]) => (
                    <Typography key={`out-${cap}`} variant="body2" color="text.secondary">
                      <b>{t('blockIo.drives')}</b>{' '}
                      {cap} → {deviceNames[bind.deviceId] ?? bind.deviceId}.{bind.capabilityId}
                    </Typography>
                  ))}
                </Stack>
              </Section>
            )}

            {controls.length > 0 && (
              <Section title={t('sections.controls')} action={
                controls.some((c) => c.id === 'on_off')
                  ? <Button size="small" onClick={allOff} disabled={offline}>{t('actions.allOff')}</Button>
                  : undefined
              }>
                <Stack spacing={2.25}>
                  {controls.map((cap) => (
                    <CapabilityControl key={cap.id} device={device} cap={cap}
                      value={device.state?.[cap.id]} onCommand={onCommand} />
                  ))}
                </Stack>
              </Section>
            )}

            {sensors.length > 0 && (
              <Section title={t('sections.sensors')}>
                <Stack spacing={2}>
                  {sensors.map((cap) => (
                    <CapabilityControl key={cap.id} device={device} cap={cap}
                      value={device.state?.[cap.id]} onCommand={onCommand} />
                  ))}
                </Stack>
              </Section>
            )}

            {device.capabilities.length === 0 && (
              <Typography variant="body2" color="text.secondary">{t('noCapabilities')}</Typography>
            )}

            {/* ML governance (Epic 2P): connect an applicable model to this device via the apply wizard. */}
            {mlCtx && mlCtx.types.length > 0 && (
              <Section title={t('ml.title')} action={
                <Button size="small" startIcon={<AutoAwesomeRoundedIcon />} onClick={() => setApplyOpen(true)}>
                  {t('ml.connect')}
                </Button>
              }>
                {governedBy.length > 0 ? (
                  <Stack spacing={0.5}>
                    {governedBy.map((b) => (
                      <Stack key={b.id} direction="row" spacing={1} alignItems="center">
                        <Typography variant="body2">{b.name}</Typography>
                        <Chip size="small" variant="outlined" color="info"
                          label={t(`ml.stage.${Math.round(b.params.stage ?? 0) >= 2 ? 'full' : Math.round(b.params.stage ?? 0) === 1 ? 'bounded' : 'shadow'}`)} />
                      </Stack>
                    ))}
                  </Stack>
                ) : (
                  <Typography variant="body2" color="text.secondary">{t('ml.hint')}</Typography>
                )}
              </Section>
            )}
          </>
        )}

        {tab === 1 && (
          <>
            {numericSensors.length > 0 && (
              <Section title={t('sections.trends')}>
                <Stack spacing={2.5}>
                  {numericSensors.map((cap) => (
                    <Box key={cap.id}>
                      <Typography variant="body2" fontWeight={600} mb={0.5}>
                        {cap.id}{cap.unit ? ` (${cap.unit})` : ''}
                      </Typography>
                      <TelemetryChart capabilityId={cap.id} deviceId={device.id} unit={cap.unit} height={180} />
                    </Box>
                  ))}
                </Stack>
              </Section>
            )}

            <Section title={t('sections.history')}>
              {history.length === 0 ? (
                <Typography variant="body2" color="text.secondary">{t('noHistory')}</Typography>
              ) : (
                <Stack spacing={1.25}>
                  {history.map((e, i) => (
                    <Stack key={`${e.timestamp}-${e.capabilityId}-${i}`} direction="row" alignItems="center"
                      spacing={1} flexWrap="wrap" useFlexGap>
                      <Typography variant="body2" fontWeight={600} sx={{ minWidth: 96 }}>{e.capabilityId}</Typography>
                      {e.kind === 'command' ? (
                        <Typography variant="body2">→ {fmtValue(e.newValue)}</Typography>
                      ) : (
                        <Stack direction="row" alignItems="center" spacing={0.5}>
                          <Typography variant="body2" color="text.secondary">{fmtValue(e.oldValue)}</Typography>
                          <ArrowRightAltRoundedIcon fontSize="small" sx={{ color: 'text.disabled' }} />
                          <Typography variant="body2">{fmtValue(e.newValue)}</Typography>
                        </Stack>
                      )}
                      <Box flex={1} />
                      {(() => {
                        const { id, name } = triggerInfo(e);
                        return <TriggerChip kind={e.triggerSource} id={id} name={name} />;
                      })()}
                      <Typography variant="caption" color="text.secondary">
                        {fmtDateTime(e.timestamp)}
                      </Typography>
                    </Stack>
                  ))}
                </Stack>
              )}
            </Section>
          </>
        )}

        {tab === 2 && (
          <>
            <Section title={t('sections.identity')}>
              {/* Friendly name (Epic 3G): user-editable; empty ⇒ type-derived label (shown as placeholder). */}
              {onSetAlias && (
                <TextField
                  size="small"
                  label={t('alias.label')}
                  value={aliasDraft}
                  placeholder={autoDeviceName(device)}
                  onChange={(e) => setAliasDraft(e.target.value)}
                  onBlur={commitAlias}
                  onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
                  helperText={t('alias.hint')}
                  sx={{ mb: 2 }}
                  fullWidth
                  InputProps={{
                    endAdornment: (device.alias || aliasDraft) ? (
                      <InputAdornment position="end">
                        <Tooltip title={t('alias.clear')}>
                          <IconButton size="small" edge="end" aria-label={t('alias.clear')} onClick={clearAlias}>
                            <RestartAltRoundedIcon fontSize="small" />
                          </IconButton>
                        </Tooltip>
                      </InputAdornment>
                    ) : undefined,
                  }}
                />
              )}

              <TextField
                select
                size="small"
                label={t('zone')}
                value={currentZone}
                onChange={(e) => onAssignZone(device.id, e.target.value || null)}
                sx={{ mb: 2 }}
                fullWidth
              >
                <MenuItem value=""><em>{t('unassigned')}</em></MenuItem>
                {zones.map((z) => (
                  <MenuItem key={z.id} value={z.id}>{z.name}</MenuItem>
                ))}
              </TextField>

              {/* Semantic type (Epic 2D): empty = auto-classified; pick to override. */}
              <TextField
                select
                size="small"
                label={t('type.label')}
                value={device.archetype ?? ''}
                onChange={(e) => onSetArchetype(device.id, e.target.value || null)}
                fullWidth
              >
                <MenuItem value=""><em>{t('type.auto', { value: device.autoArchetype ?? t('type.unknown') })}</em></MenuItem>
                {archetypes.map((a) => (
                  <MenuItem key={a} value={a}>{a.replace(/_/g, ' ')}</MenuItem>
                ))}
              </TextField>
            </Section>

            {/* Energy accounting (Epic 3C-D): the toggle, the nameplate watts and the role behind kWh totals. */}
            <EnergyProfileEditor device={device} />

            {/* Load-shedding profile (Epic 3C-LM): only for devices with a writable on_off capability. */}
            <LoadSheddingProfileEditor device={device} />

            <Section title={t('sections.tech')}>
              <Stack spacing={0.75}>
                <TechRow label={t('tech.source')} value={device.adapterSource} />
                {device.model && <TechRow label={t('tech.model')} value={device.model} />}
                {isCrypticName(device.name) && <TechRow label={t('tech.rawName')} value={device.name} mono />}
                <TechRow label={t('tech.id')} value={device.id} mono />
                {device.lastUpdated && (
                  <TechRow label={t('tech.updated')} value={fmtDateTime(device.lastUpdated)} />
                )}
              </Stack>
            </Section>
          </>
        )}
      </Box>

      {mlCtx && (
        <MlApplyWizard
          open={applyOpen}
          device={device}
          tasks={mlCtx.tasks}
          catalog={mlCtx.catalog}
          devices={mlCtx.devices}
          zones={zones}
          onClose={() => setApplyOpen(false)}
          onCreated={() => setApplyOpen(false)}
        />
      )}
    </Box>
  );
}

function Section({ title, action, children }: { title: string; action?: React.ReactNode; children: React.ReactNode }) {
  return (
    <Box mb={3}>
      <Stack direction="row" alignItems="center" justifyContent="space-between" mb={1.5}>
        <Typography variant="overline" color="text.secondary" letterSpacing={1}>{title}</Typography>
        {action}
      </Stack>
      {children}
    </Box>
  );
}

/** Label/value line for the technical block — long ids wrap instead of widening the panel. */
function TechRow({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <Stack direction="row" spacing={1} alignItems="baseline">
      <Typography variant="caption" color="text.secondary" sx={{ minWidth: 104, flexShrink: 0 }}>{label}</Typography>
      <Typography
        variant="caption"
        sx={{ wordBreak: 'break-all', fontFamily: mono ? 'monospace' : undefined }}
      >
        {value}
      </Typography>
    </Stack>
  );
}
