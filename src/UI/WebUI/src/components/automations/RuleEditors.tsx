// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Per-row editors for the Automations rule builder: one trigger, one condition, one action. They were
// factored out of the removed Flow page so the Automations dialog can author *several* of each (multi-
// trigger OR / multi-condition AND / multi-action). Type dropdowns are localized (a bare "DeviceState"
// was exactly the confusing part) via the `automations` namespace; operator/value codes reuse the shared
// `operators` vocabulary through optionLabels.

import { Stack, TextField, MenuItem } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { RuleTrigger, RuleCondition, RuleAction } from '../../api/automations';
import { CapabilityDevice } from '../../api/capabilityDevices';
import { Scene } from '../../api/scenes';
import { optionValueLabel } from '../../i18n/optionLabels';
import { OPERATORS, parseValue, fmt } from './ruleValues';

type Caps = (id?: string | null) => CapabilityDevice['capabilities'];

const TRIGGER_TYPES: RuleTrigger['type'][] = ['DeviceState', 'Time', 'Sun'];
const CONDITION_TYPES: RuleCondition['type'][] = ['DeviceState', 'TimeOfDay', 'Sun', 'Mode'];
const ACTION_TYPES: RuleAction['type'][] = ['Command', 'Delay', 'Notify', 'Scene'];
const SUN_EVENTS = ['Sunrise', 'Sunset'] as const;
const MODES = ['Home', 'Away', 'Night', 'Vacation'];

export function TriggerEditor({ devices, caps, value, onChange }: {
  devices: CapabilityDevice[]; caps: Caps;
  value: RuleTrigger; onChange: (t: RuleTrigger) => void;
}) {
  const { t } = useTranslation('automations');
  return (
    <Stack spacing={1.5}>
      <TextField select label={t('editor.trigger.type')} size="small" value={value.type}
        onChange={(e) => onChange({ ...value, type: e.target.value as RuleTrigger['type'] })}>
        {TRIGGER_TYPES.map((ty) => <MenuItem key={ty} value={ty}>{t(`editor.trigger.types.${ty}`)}</MenuItem>)}
      </TextField>
      {value.type === 'DeviceState' && (
        <>
          <TextField select label={t('editor.trigger.device')} size="small" value={value.deviceId ?? ''}
            onChange={(e) => onChange({ ...value, deviceId: e.target.value, capabilityId: '' })}>
            {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
          </TextField>
          <TextField select label={t('editor.trigger.capability')} size="small" value={value.capabilityId ?? ''} disabled={!value.deviceId}
            onChange={(e) => onChange({ ...value, capabilityId: e.target.value })}>
            {caps(value.deviceId).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
          </TextField>
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

export function ConditionEditor({ devices, caps, value, onChange }: {
  devices: CapabilityDevice[]; caps: Caps;
  value: RuleCondition; onChange: (c: RuleCondition) => void;
}) {
  const { t } = useTranslation('automations');
  return (
    <Stack spacing={1.5}>
      <TextField select label={t('editor.condition.type')} size="small" value={value.type}
        onChange={(e) => onChange({ ...value, type: e.target.value as RuleCondition['type'] })}>
        {CONDITION_TYPES.map((ty) => <MenuItem key={ty} value={ty}>{t(`editor.condition.types.${ty}`)}</MenuItem>)}
      </TextField>
      {value.type === 'DeviceState' && (
        <>
          <TextField select label={t('editor.condition.device')} size="small" value={value.deviceId ?? ''}
            onChange={(e) => onChange({ ...value, deviceId: e.target.value, capabilityId: '' })}>
            {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
          </TextField>
          <TextField select label={t('editor.condition.capability')} size="small" value={value.capabilityId ?? ''} disabled={!value.deviceId}
            onChange={(e) => onChange({ ...value, capabilityId: e.target.value })}>
            {caps(value.deviceId).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
          </TextField>
          <Stack direction="row" spacing={1}>
            <TextField select label={t('editor.condition.op')} size="small" value={value.operator ?? 'eq'} sx={{ minWidth: 168 }}
              onChange={(e) => onChange({ ...value, operator: e.target.value })}>
              {OPERATORS.filter((o) => o !== 'changed').map((o) => <MenuItem key={o} value={o}>{optionValueLabel(t, o)}</MenuItem>)}
            </TextField>
            <TextField label={t('editor.condition.value')} size="small" fullWidth value={fmt(value.value)}
              onChange={(e) => onChange({ ...value, value: parseValue(e.target.value) })} />
          </Stack>
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

export function ActionEditor({ devices, caps, scenes = [], value, onChange }: {
  devices: CapabilityDevice[]; caps: Caps; scenes?: Scene[];
  value: RuleAction; onChange: (a: RuleAction) => void;
}) {
  const { t } = useTranslation('automations');
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
          <TextField select label={t('editor.action.device')} size="small" value={value.deviceId ?? ''}
            onChange={(e) => onChange({ ...value, deviceId: e.target.value, set: {} })}>
            {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
          </TextField>
          <Stack direction="row" spacing={1}>
            <TextField select label={t('editor.action.capability')} size="small" value={setKey} disabled={!value.deviceId} sx={{ flex: 1 }}
              onChange={(e) => onChange({ ...value, set: { [e.target.value]: setVal === '' ? true : setVal } })}>
              {caps(value.deviceId).filter((c) => c.writable).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
            </TextField>
            <TextField label={t('editor.action.value')} size="small" sx={{ width: 120 }} disabled={!setKey} value={fmt(setVal)}
              onChange={(e) => onChange({ ...value, set: { [setKey]: parseValue(e.target.value) } })} />
          </Stack>
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
    </Stack>
  );
}
