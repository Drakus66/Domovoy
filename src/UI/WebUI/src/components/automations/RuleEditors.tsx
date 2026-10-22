// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Per-row editors for the Automations rule builder: one trigger, one condition, one action. They were
// factored out of the removed Flow page so the Automations dialog can author *several* of each (multi-
// trigger OR / multi-condition AND / multi-action). Type dropdowns are localized (a bare "DeviceState"
// was exactly the confusing part) via the `automations` namespace; operator/value codes reuse the shared
// `operators` vocabulary through optionLabels.
//
// Epic 3G — authoring UX (WebUI-only, no backend change): every device-state row now uses <TargetPicker>,
// a context-aware picker that (a) lets you scope to a whole zone, not just one device — the backend already
// evaluates a zone-scoped match against any device in the zone — (b) shows the device's zone / the "affects
// N devices" coverage right in the picker, and (c) offers capabilities by their human label. A <ReadablePreview>
// under each row renders the row as a sentence ("when in «Bedroom» temperature is below 18").

import { useState } from 'react';
import {
  Stack, TextField, MenuItem, Paper, IconButton, Button, Typography, Box, Collapse,
  ToggleButton, ToggleButtonGroup,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded';
import { useTranslation } from 'react-i18next';
import { RuleTrigger, RuleCondition, RuleAction } from '../../api/automations';
import { CapabilityDevice, Capability } from '../../api/capabilityDevices';
import { Zone } from '../../api/zones';
import { Scene } from '../../api/scenes';
import { optionValueLabel } from '../../i18n/optionLabels';
import { capabilityLabel } from '../devices/deviceVisuals';
import { deviceLabel as deviceDisplayName } from '../devices/deviceNaming';
import { OPERATORS, parseValue, fmt } from './ruleValues';
import {
  readableMatch, zoneNameOf, zoneCoverage, capabilitiesInZone, zonesWithDevices,
} from './ruleReadable';

// `caps` is still accepted for backward compatibility (RequiredExpressionEditor / callers pass it) but the
// device-state rows now derive capabilities from `devices` + `zones` through <TargetPicker>, so it is unused.
type Caps = (id?: string | null) => CapabilityDevice['capabilities'];

const TRIGGER_TYPES: RuleTrigger['type'][] = ['DeviceState', 'Time', 'Sun'];
const CONDITION_TYPES: RuleCondition['type'][] = ['DeviceState', 'TimeOfDay', 'Sun', 'Mode'];
const ACTION_TYPES: RuleAction['type'][] = ['Command', 'Delay', 'Notify', 'Scene', 'WaitForEvent'];
const SUN_EVENTS = ['Sunrise', 'Sunset'] as const;
const MODES = ['Home', 'Away', 'Night', 'Vacation'];

/** Normalized device-state target: a specific device OR a whole zone, plus the capability being addressed. */
interface TargetValue {
  deviceId?: string | null;
  zoneId?: string | null;
  capabilityId?: string | null;
}

/**
 * Epic 3G context-aware target picker. Scope toggle (Device / Zone — zone only when the home has zones and
 * `allowZone`), a device or zone dropdown that carries context (the device's zone; the zone's device count),
 * and a capability dropdown offered by human label. A caption shows the picked device's zone, or — in zone
 * scope — how many devices the target actually reaches ("affects N devices"). `writableOnly` narrows the
 * capability list to actuatable ones (command targets).
 */
function TargetPicker({
  devices, zones, value, onChange, allowZone = true, writableOnly = false, deviceLabel, capLabel,
}: {
  devices: CapabilityDevice[];
  zones: Zone[];
  value: TargetValue;
  onChange: (v: TargetValue) => void;
  allowZone?: boolean;
  writableOnly?: boolean;
  deviceLabel: string;
  capLabel: string;
}) {
  const { t } = useTranslation(['automations', 'devices']);
  const canZone = allowZone && zones.length > 0;
  const [mode, setMode] = useState<'device' | 'zone'>(canZone && value.zoneId ? 'zone' : 'device');
  const zoneMode = canZone && mode === 'zone';

  const selectedDevice = devices.find((d) => d.id === value.deviceId);
  const zoneOptions = zonesWithDevices(zones, devices);
  const capabilityOptions: Capability[] = zoneMode
    ? capabilitiesInZone(devices, value.zoneId, { writableOnly })
    : (selectedDevice?.capabilities ?? []).filter((c) => !writableOnly || c.writable);
  const coverage = zoneCoverage(devices, value.zoneId, value.capabilityId);

  const switchMode = (next: 'device' | 'zone' | null) => {
    if (!next || next === mode) return;
    setMode(next);
    // Mutually exclusive scope; capability is re-picked against the new option set to avoid a stale binding.
    onChange(next === 'zone'
      ? { zoneId: value.zoneId ?? '', deviceId: null, capabilityId: '' }
      : { deviceId: value.deviceId ?? '', zoneId: null, capabilityId: '' });
  };

  return (
    <Stack spacing={1}>
      {canZone && (
        <ToggleButtonGroup size="small" exclusive value={mode} onChange={(_, m) => switchMode(m)}
          sx={{ alignSelf: 'flex-start' }}>
          <ToggleButton value="device" sx={{ px: 1.5, py: 0.25, textTransform: 'none' }}>{t('target.modeDevice')}</ToggleButton>
          <ToggleButton value="zone" sx={{ px: 1.5, py: 0.25, textTransform: 'none' }}>{t('target.modeZone')}</ToggleButton>
        </ToggleButtonGroup>
      )}

      {zoneMode ? (
        <TextField select label={t('target.zone')} size="small" value={value.zoneId ?? ''}
          SelectProps={{ renderValue: (v) => zoneNameOf(zones, String(v)) }}
          onChange={(e) => onChange({ zoneId: e.target.value, deviceId: null, capabilityId: '' })}>
          {zoneOptions.map(({ zone, count }) => (
            <MenuItem key={zone.id} value={zone.id}>
              <Box>
                <Typography variant="body2">{zone.name}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {t('target.deviceCount', { count })}
                </Typography>
              </Box>
            </MenuItem>
          ))}
        </TextField>
      ) : (
        <TextField select label={deviceLabel} size="small" value={value.deviceId ?? ''}
          SelectProps={{ renderValue: (v) => { const d = devices.find((x) => x.id === v); return d ? deviceDisplayName(d) : ''; } }}
          onChange={(e) => onChange({ deviceId: e.target.value, zoneId: null, capabilityId: '' })}>
          {devices.map((d) => (
            <MenuItem key={d.id} value={d.id}>
              <Box>
                <Typography variant="body2">{deviceDisplayName(d)}</Typography>
                <Typography variant="caption" color="text.secondary">{zoneNameOf(zones, d.zoneId)}</Typography>
              </Box>
            </MenuItem>
          ))}
        </TextField>
      )}

      <TextField select label={capLabel} size="small" value={value.capabilityId ?? ''}
        disabled={zoneMode ? !value.zoneId : !value.deviceId}
        SelectProps={{ renderValue: (v) => capabilityLabel(String(v)) }}
        onChange={(e) => onChange({ ...value, capabilityId: e.target.value })}>
        {capabilityOptions.map((c) => (
          <MenuItem key={c.id} value={c.id}>
            <Box>
              <Typography variant="body2">{capabilityLabel(c.id)}</Typography>
              {capabilityLabel(c.id) !== c.id && (
                <Typography variant="caption" color="text.secondary">{c.id}</Typography>
              )}
            </Box>
          </MenuItem>
        ))}
      </TextField>

      <TargetContext zoneMode={zoneMode} coverage={coverage} hasCapability={!!value.capabilityId}
        deviceZone={selectedDevice ? zoneNameOf(zones, selectedDevice.zoneId) : null} />
    </Stack>
  );
}

/** The context caption under the picker: the device's zone, or the zone-scope coverage ("affects N devices"). */
function TargetContext({ zoneMode, coverage, hasCapability, deviceZone }: {
  zoneMode: boolean; coverage: number; hasCapability: boolean; deviceZone: string | null;
}) {
  const { t } = useTranslation('automations');
  if (zoneMode) {
    const text = !hasCapability
      ? t('target.deviceCount', { count: coverage })
      : coverage === 0 ? t('target.coverageNone') : t('target.coverage', { count: coverage });
    return <Typography variant="caption" color={coverage === 0 && hasCapability ? 'warning.main' : 'text.secondary'}>{text}</Typography>;
  }
  if (deviceZone) return <Typography variant="caption" color="text.secondary">{t('target.inZone', { zone: deviceZone })}</Typography>;
  return null;
}

/** Live plain-language rendering of the row being edited (Epic 3G). Hidden until the row selects a target. */
function ReadablePreview({ text }: { text: string }) {
  if (!text.trim()) return null;
  return (
    <Typography variant="caption" color="text.secondary"
      sx={{ display: 'flex', alignItems: 'center', gap: 0.5, fontStyle: 'italic', mt: 0.25 }}>
      <AutoAwesomeRoundedIcon sx={{ fontSize: 14, opacity: 0.6 }} />
      {text}
    </Typography>
  );
}

export function TriggerEditor({ devices, zones = [], value, onChange }: {
  devices: CapabilityDevice[]; caps?: Caps; zones?: Zone[];
  value: RuleTrigger; onChange: (t: RuleTrigger) => void;
}) {
  const { t } = useTranslation(['automations', 'devices']);
  return (
    <Stack spacing={1.5}>
      <TextField select label={t('editor.trigger.type')} size="small" value={value.type}
        onChange={(e) => onChange({ ...value, type: e.target.value as RuleTrigger['type'] })}>
        {TRIGGER_TYPES.map((ty) => <MenuItem key={ty} value={ty}>{t(`editor.trigger.types.${ty}`)}</MenuItem>)}
      </TextField>
      {value.type === 'DeviceState' && (
        <>
          <TargetPicker devices={devices} zones={zones}
            value={{ deviceId: value.deviceId, zoneId: value.zoneId, capabilityId: value.capabilityId }}
            onChange={(v) => onChange({ ...value, deviceId: v.deviceId, zoneId: v.zoneId, capabilityId: v.capabilityId })}
            deviceLabel={t('editor.trigger.device')} capLabel={t('editor.trigger.capability')} />
          <Stack direction="row" spacing={1}>
            <TextField select label={t('editor.trigger.op')} size="small" value={value.operator ?? 'eq'} sx={{ minWidth: 168 }}
              onChange={(e) => onChange({ ...value, operator: e.target.value })}>
              {OPERATORS.map((o) => <MenuItem key={o} value={o}>{optionValueLabel(t, o)}</MenuItem>)}
            </TextField>
            <TextField label={t('editor.trigger.value')} size="small" fullWidth disabled={value.operator === 'changed'}
              helperText={t('editor.valueHelper')}
              value={value.operator === 'changed' ? '' : fmt(value.value)}
              onChange={(e) => onChange({ ...value, value: parseValue(e.target.value) })} />
          </Stack>
          <ReadablePreview text={readableMatch(devices, zones, value)} />
        </>
      )}
      {value.type === 'Time' && (
        <TextField label={t('editor.trigger.cron')} size="small" value={value.cron ?? ''}
          onChange={(e) => onChange({ ...value, cron: e.target.value })} />
      )}
      {value.type === 'Sun' && (
        <Stack direction="row" spacing={1}>
          <TextField select label={t('editor.trigger.event')} size="small" value={value.sun ?? 'Sunset'} fullWidth
            onChange={(e) => onChange({ ...value, sun: e.target.value as RuleTrigger['sun'] })}>
            {SUN_EVENTS.map((s) => <MenuItem key={s} value={s}>{t(`editor.sun.${s}`)}</MenuItem>)}
          </TextField>
          <TextField type="number" label={t('editor.trigger.offsetMin')} size="small" value={value.offsetMinutes ?? 0} sx={{ width: 130 }}
            onChange={(e) => onChange({ ...value, offsetMinutes: Number(e.target.value) || 0 })} />
        </Stack>
      )}
    </Stack>
  );
}

export function ConditionEditor({ devices, zones = [], value, onChange }: {
  devices: CapabilityDevice[]; caps?: Caps; zones?: Zone[];
  value: RuleCondition; onChange: (c: RuleCondition) => void;
}) {
  const { t } = useTranslation(['automations', 'devices']);
  return (
    <Stack spacing={1.5}>
      <TextField select label={t('editor.condition.type')} size="small" value={value.type}
        onChange={(e) => onChange({ ...value, type: e.target.value as RuleCondition['type'] })}>
        {CONDITION_TYPES.map((ty) => <MenuItem key={ty} value={ty}>{t(`editor.condition.types.${ty}`)}</MenuItem>)}
      </TextField>
      {value.type === 'DeviceState' && (
        <>
          <TargetPicker devices={devices} zones={zones}
            value={{ deviceId: value.deviceId, zoneId: value.zoneId, capabilityId: value.capabilityId }}
            onChange={(v) => onChange({ ...value, deviceId: v.deviceId, zoneId: v.zoneId, capabilityId: v.capabilityId })}
            deviceLabel={t('editor.condition.device')} capLabel={t('editor.condition.capability')} />
          <Stack direction="row" spacing={1}>
            <TextField select label={t('editor.condition.op')} size="small" value={value.operator ?? 'eq'} sx={{ minWidth: 168 }}
              onChange={(e) => onChange({ ...value, operator: e.target.value })}>
              {OPERATORS.filter((o) => o !== 'changed').map((o) => <MenuItem key={o} value={o}>{optionValueLabel(t, o)}</MenuItem>)}
            </TextField>
            <TextField label={t('editor.condition.value')} size="small" fullWidth value={fmt(value.value)}
              onChange={(e) => onChange({ ...value, value: parseValue(e.target.value) })} />
          </Stack>
          <ReadablePreview text={readableMatch(devices, zones, value)} />
        </>
      )}
      {value.type === 'TimeOfDay' && (
        <Stack direction="row" spacing={1}>
          <TextField label={t('editor.condition.fromTime')} size="small" value={value.fromTime ?? ''}
            onChange={(e) => onChange({ ...value, fromTime: e.target.value })} />
          <TextField label={t('editor.condition.toTime')} size="small" value={value.toTime ?? ''}
            onChange={(e) => onChange({ ...value, toTime: e.target.value })} />
        </Stack>
      )}
      {value.type === 'Sun' && (
        <TextField select label={t('editor.condition.daylight')} size="small" value={value.dark === false ? 'light' : 'dark'}
          onChange={(e) => onChange({ ...value, dark: e.target.value === 'dark' })}>
          <MenuItem value="dark">{t('editor.condition.whileDark')}</MenuItem>
          <MenuItem value="light">{t('editor.condition.whileLight')}</MenuItem>
        </TextField>
      )}
      {value.type === 'Mode' && (
        <TextField select label={t('editor.condition.mode')} size="small" value={value.mode ?? 'Home'}
          onChange={(e) => onChange({ ...value, mode: e.target.value })}>
          {MODES.map((m) => <MenuItem key={m} value={m}>{t(`editor.condition.modes.${m}`)}</MenuItem>)}
        </TextField>
      )}
    </Stack>
  );
}

export function ActionEditor({ devices, zones = [], scenes = [], value, onChange, nestable = true }: {
  devices: CapabilityDevice[]; caps?: Caps; zones?: Zone[]; scenes?: Scene[];
  value: RuleAction; onChange: (a: RuleAction) => void;
  /** Gates the on-error/on-timeout branch UI, bounding nesting to exactly one level. Default true. */
  nestable?: boolean;
}) {
  const { t } = useTranslation(['automations', 'devices']);
  // Default collapsed unless the action already carries an error branch (editing an existing rule).
  const [errorOpen, setErrorOpen] = useState(!!value.onError?.length);
  const setKey = Object.keys(value.set ?? {})[0] ?? '';
  const setVal = setKey ? (value.set as Record<string, unknown>)[setKey] : '';
  return (
    <Stack spacing={1.5}>
      <TextField select label={t('editor.action.type')} size="small" value={value.type}
        onChange={(e) => onChange({ ...value, type: e.target.value as RuleAction['type'] })}>
        {ACTION_TYPES.map((ty) => <MenuItem key={ty} value={ty}>{t(`editor.action.types.${ty}`)}</MenuItem>)}
      </TextField>
      {value.type === 'Command' && (
        <>
          {/* Command acts on one device — the wire model (RuleAction) has no zone target — but the picker
              still surfaces the device's zone as context. */}
          <TargetPicker devices={devices} zones={zones} allowZone={false} writableOnly
            value={{ deviceId: value.deviceId, capabilityId: setKey }}
            onChange={(v) => {
              const capId = v.capabilityId ?? '';
              const nextVal = capId && capId === setKey ? setVal : true;
              onChange({ ...value, deviceId: v.deviceId ?? '', set: capId ? { [capId]: nextVal } : {} });
            }}
            deviceLabel={t('editor.action.device')} capLabel={t('editor.action.capability')} />
          <TextField label={t('editor.action.value')} size="small" sx={{ width: 160 }} disabled={!setKey} value={fmt(setVal)}
            onChange={(e) => onChange({ ...value, set: { [setKey]: parseValue(e.target.value) } })} />
        </>
      )}
      {value.type === 'Delay' && (
        <TextField type="number" label={t('editor.action.seconds')} size="small" value={value.delaySeconds ?? 0}
          onChange={(e) => onChange({ ...value, delaySeconds: Number(e.target.value) || 0 })} />
      )}
      {value.type === 'Notify' && (
        <TextField label={t('editor.action.message')} size="small" fullWidth multiline minRows={2} value={value.message ?? ''}
          helperText={t('editor.action.messageHelper')}
          onChange={(e) => onChange({ ...value, message: e.target.value })} />
      )}
      {value.type === 'Scene' && (
        <TextField select label={t('editor.action.scene')} size="small" value={value.sceneId ?? ''}
          helperText={scenes.length === 0 ? t('editor.action.noScenes') : t('editor.action.sceneHelper')}
          onChange={(e) => onChange({ ...value, sceneId: e.target.value })}>
          {scenes.map((s) => <MenuItem key={s.id} value={s.id}>{s.name}</MenuItem>)}
        </TextField>
      )}
      {value.type === 'WaitForEvent' && (
        <>
          <TargetPicker devices={devices} zones={zones}
            value={{ deviceId: value.waitDeviceId, zoneId: value.waitZoneId, capabilityId: value.waitCapabilityId }}
            onChange={(v) => onChange({ ...value, waitDeviceId: v.deviceId, waitZoneId: v.zoneId, waitCapabilityId: v.capabilityId })}
            deviceLabel={t('editor.action.waitDevice')} capLabel={t('editor.action.waitCapability')} />
          <Stack direction="row" spacing={1}>
            <TextField select label={t('editor.action.waitOp')} size="small" value={value.waitOperator ?? 'eq'} sx={{ minWidth: 168 }}
              onChange={(e) => onChange({ ...value, waitOperator: e.target.value })}>
              {OPERATORS.filter((o) => o !== 'changed').map((o) => <MenuItem key={o} value={o}>{optionValueLabel(t, o)}</MenuItem>)}
            </TextField>
            <TextField label={t('editor.action.waitValue')} size="small" fullWidth value={fmt(value.waitValue)}
              onChange={(e) => onChange({ ...value, waitValue: parseValue(e.target.value) })} />
          </Stack>
          <ReadablePreview text={readableMatch(devices, zones, {
            deviceId: value.waitDeviceId, zoneId: value.waitZoneId, capabilityId: value.waitCapabilityId,
            operator: value.waitOperator, value: value.waitValue,
          })} />
          <TextField type="number" label={t('editor.action.timeoutSeconds')} size="small" value={value.timeoutSeconds ?? 0}
            onChange={(e) => onChange({ ...value, timeoutSeconds: Number(e.target.value) || 0 })} />
          {nestable && (
            <Box>
              <Typography variant="overline" color="text.secondary" display="block">
                {t('editor.action.onTimeout.title')}
              </Typography>
              <Typography variant="caption" color="text.secondary" display="block" sx={{ mb: 1 }}>
                {t('editor.action.onTimeout.hint')}
              </Typography>
              <ActionListEditor devices={devices} zones={zones} scenes={scenes} actions={value.onTimeout ?? []}
                onChange={(a) => onChange({ ...value, onTimeout: a })} nestable={false} />
            </Box>
          )}
        </>
      )}

      {/* Every action type can fail at runtime — the error branch is available regardless of `value.type`. */}
      {nestable && (
        <Box>
          <Button size="small" onClick={() => setErrorOpen((v) => !v)}
            endIcon={
              <ExpandMoreRoundedIcon fontSize="small"
                sx={{ transition: 'transform 0.2s', transform: errorOpen ? 'rotate(180deg)' : 'none' }} />
            }>
            {t('editor.action.onError.title')}
          </Button>
          <Collapse in={errorOpen} timeout="auto">
            <Box mt={1}>
              <Typography variant="caption" color="text.secondary" display="block" sx={{ mb: 1 }}>
                {t('editor.action.onError.hint')}
              </Typography>
              <ActionListEditor devices={devices} zones={zones} scenes={scenes} actions={value.onError ?? []}
                onChange={(a) => onChange({ ...value, onError: a })} nestable={false} />
            </Box>
          </Collapse>
        </Box>
      )}
    </Stack>
  );
}

/**
 * A nested list of actions for a branch (WaitForEvent's on-timeout, any action's on-error). `nestable` is
 * always passed through as `false` from `ActionEditor` above so branch actions never render their own
 * branch UI — this is what bounds nesting to exactly one level.
 */
export function ActionListEditor({ devices, zones = [], scenes = [], actions, onChange, nestable }: {
  devices: CapabilityDevice[]; caps?: Caps; zones?: Zone[]; scenes?: Scene[];
  actions: RuleAction[]; onChange: (a: RuleAction[]) => void; nestable: boolean;
}) {
  const { t } = useTranslation('automations');
  return (
    <Stack spacing={1}>
      {actions.map((a, i) => (
        <Paper key={i} variant="outlined" sx={{ p: 1.5, pr: 5, position: 'relative', borderRadius: 2 }}>
          <IconButton size="small" onClick={() => onChange(actions.filter((_, x) => x !== i))}
            sx={{ position: 'absolute', top: 6, right: 6 }}>
            <DeleteOutlineRoundedIcon fontSize="small" />
          </IconButton>
          <ActionEditor devices={devices} zones={zones} scenes={scenes} value={a} nestable={nestable}
            onChange={(v) => onChange(actions.map((x, xi) => (xi === i ? v : x)))} />
        </Paper>
      ))}
      <Button size="small" startIcon={<AddRoundedIcon />} onClick={() => onChange([...actions, { type: 'Command', set: {} }])}>
        {t('editor.action.addBranchAction')}
      </Button>
    </Stack>
  );
}
