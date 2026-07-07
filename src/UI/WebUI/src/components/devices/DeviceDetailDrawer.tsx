// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import {
  Box, Drawer, Stack, Typography, IconButton, Chip, Divider, Button,
  TextField, MenuItem, Tooltip,
} from '@mui/material';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import CircleIcon from '@mui/icons-material/Circle';
import ArrowRightAltRoundedIcon from '@mui/icons-material/ArrowRightAltRounded';
import {
  CapabilityDevice, capabilityDevicesApi, isUnassignedZone, DEVICE_ARCHETYPES, effectiveArchetype,
} from '../../api/capabilityDevices';
import type { Zone } from '../../api/zones';
import { historyApi, EventLogEntry } from '../../api/history';
import { automationsApi } from '../../api/automations';
import { blocksApi, PortBinding } from '../../api/blocks';
import CapabilityControl, { type CommandFn } from './CapabilityControls';
import { describeDevice } from './deviceVisuals';
import { fmtDateTime } from '../../i18n/format';
import TelemetryChart from '../charts/TelemetryChart';

const TRIGGER_COLOR: Record<string, 'primary' | 'secondary' | 'default' | 'info'> = {
  user: 'primary', rule: 'secondary', ml: 'info', device: 'default',
};

const fmtValue = (v: unknown): string => {
  if (v === null || v === undefined || v === '') return '—';
  if (typeof v === 'boolean') return i18n.t(v ? 'devices:status.on' : 'devices:status.off');
  return String(v);
};

export type AssignZoneFn = (deviceId: string, zoneId: string | null) => void;
export type SetArchetypeFn = (deviceId: string, archetype: string | null) => void;

/** Sliding panel with the full per-capability control surface for one device. */
export default function DeviceDetailDrawer({
  device, zones, open, onClose, onCommand, onAssignZone, onSetArchetype,
}: {
  device: CapabilityDevice | null;
  zones: Zone[];
  open: boolean;
  onClose: () => void;
  onCommand: CommandFn;
  onAssignZone: AssignZoneFn;
  onSetArchetype: SetArchetypeFn;
}) {
  return (
    <Drawer
      anchor="right"
      open={open && !!device}
      onClose={onClose}
      PaperProps={{ sx: { width: { xs: '100%', sm: 380 }, maxWidth: '100%' } }}
    >
      {device && (
        <DrawerBody
          device={device} zones={zones} onClose={onClose}
          onCommand={onCommand} onAssignZone={onAssignZone} onSetArchetype={onSetArchetype}
        />
      )}
    </Drawer>
  );
}

function DrawerBody({
  device, zones, onClose, onCommand, onAssignZone, onSetArchetype,
}: {
  device: CapabilityDevice;
  zones: Zone[];
  onClose: () => void;
  onCommand: CommandFn;
  onAssignZone: AssignZoneFn;
  onSetArchetype: SetArchetypeFn;
}) {
  const { t } = useTranslation('devices');
  const { accent, Icon } = describeDevice(device);
  const offline = !device.isOnline;
  const currentZone = isUnassignedZone(device.zoneId) ? '' : device.zoneId;

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
  const explainRule = (e: EventLogEntry): string | null => {
    if (e.triggerSource !== 'rule') return null;
    const id = e.ruleId || e.correlationId || '';
    return ruleNames[id] ?? (id ? t('aRule') : null);
  };

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
        setDeviceNames(Object.fromEntries(devs.map((d) => [d.id, d.name])));
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
    <Box sx={{ p: 2.5, height: '100%', display: 'flex', flexDirection: 'column' }}>
      <Stack direction="row" alignItems="flex-start" spacing={1.5} mb={2}>
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
            {device.name}
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

      <Typography variant="caption" color="text.secondary" mb={2}>
        {device.adapterSource}{device.model ? ` · ${device.model}` : ''}
      </Typography>

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
        sx={{ mb: 2 }}
        fullWidth
      >
        <MenuItem value=""><em>{t('type.auto', { value: device.autoArchetype ?? t('type.unknown') })}</em></MenuItem>
        {DEVICE_ARCHETYPES.map((a) => (
          <MenuItem key={a} value={a}>{a.replace(/_/g, ' ')}</MenuItem>
        ))}
      </TextField>

      <Box sx={{ flex: 1, overflowY: 'auto', mx: -0.5, px: 0.5 }}>
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

        {numericSensors.length > 0 && (
          <Section title={t('sections.trends')}>
            <Stack spacing={2.5}>
              {numericSensors.map((cap) => (
                <Box key={cap.id}>
                  <Typography variant="body2" fontWeight={600} mb={0.5}>
                    {cap.id}{cap.unit ? ` (${cap.unit})` : ''}
                  </Typography>
                  <TelemetryChart capabilityId={cap.id} deviceId={device.id} unit={cap.unit} height={160} />
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
                  {explainRule(e) ? (
                    <Tooltip title={t('historyCausedBy', { rule: explainRule(e) })}>
                      <Chip size="small" variant="outlined" color="secondary" label={t('historyVia', { rule: explainRule(e) })} />
                    </Tooltip>
                  ) : (
                    <Chip size="small" variant="outlined" label={e.triggerSource}
                      color={TRIGGER_COLOR[e.triggerSource] ?? 'default'} />
                  )}
                  <Typography variant="caption" color="text.secondary">
                    {fmtDateTime(e.timestamp)}
                  </Typography>
                </Stack>
              ))}
            </Stack>
          )}
        </Section>
      </Box>

      <Divider sx={{ mt: 2 }} />
      <Stack spacing={0.25} pt={1.5}>
        <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace', wordBreak: 'break-all' }}>
          {device.id}
        </Typography>
        {device.lastUpdated && (
          <Typography variant="caption" color="text.secondary">
            {t('updated', { when: fmtDateTime(device.lastUpdated) })}
          </Typography>
        )}
      </Stack>
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
