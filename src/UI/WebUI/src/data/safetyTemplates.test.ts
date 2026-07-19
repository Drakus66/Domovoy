// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import {
  SAFETY_TEMPLATES, SafetyTemplate, buildDraftFromTemplate, isTemplateRecommended,
  recommendedTemplateIds, isDraftValid, emptyDraft, draftFromRule,
} from './safetyTemplates';
import { AutomationRule } from '../api/automations';
import { CapabilityDevice } from '../api/capabilityDevices';

const device = (id: string, caps: string[]): CapabilityDevice => ({
  id, name: id, adapterSource: 'test', zoneId: '', state: {}, isOnline: true, lastUpdated: '',
  capabilities: caps.map((c) => ({ id: c, kind: 'Number', writable: false })),
});

const ruleWatching = (cap: string): AutomationRule => ({
  id: `r-${cap}`, name: cap, status: 'Active', isProtected: false,
  triggers: [{ type: 'DeviceState', capabilityId: cap, operator: 'lt', value: 5 }],
  conditions: [], actions: [], createdAt: '', updatedAt: '',
});

const antiFreeze = SAFETY_TEMPLATES.find((t) => t.id === 'antiFreezeHeat') as SafetyTemplate;

describe('safety templates', () => {
  it('recommends a template when a matching sensor exists and no rule covers it', () => {
    const devices = [device('d1', ['temperature', 'humidity'])];
    expect(isTemplateRecommended(antiFreeze, devices, [])).toBe(true);
  });

  it('does not recommend when no device exposes the trigger capability', () => {
    const devices = [device('d1', ['on_off'])];
    expect(isTemplateRecommended(antiFreeze, devices, [])).toBe(false);
  });

  it('does not recommend when an existing rule already watches that capability', () => {
    const devices = [device('d1', ['temperature'])];
    expect(isTemplateRecommended(antiFreeze, devices, [ruleWatching('temperature')])).toBe(false);
  });

  it('recommendedTemplateIds returns only the gap-closing templates', () => {
    const devices = [device('d1', ['temperature']), device('d2', ['co2'])];
    const ids = recommendedTemplateIds(SAFETY_TEMPLATES, devices, [ruleWatching('temperature')]);
    // temperature is already covered → its templates drop out; co2 templates stay.
    expect(ids.has('antiFreezeHeat')).toBe(false);
    expect(ids.has('co2Ventilation')).toBe(true);
    expect(ids.has('co2Alert')).toBe(true);
    // no smoke/leak sensor present → those never recommended
    expect(ids.has('smokeUnlock')).toBe(false);
  });

  it('auto-selects the trigger device only when exactly one matches', () => {
    const one = buildDraftFromTemplate(antiFreeze, [device('d1', ['temperature'])]);
    expect(one.triggers[0].deviceId).toBe('d1');
    expect(one.triggers[0].capabilityId).toBe('temperature');

    const many = buildDraftFromTemplate(antiFreeze, [device('d1', ['temperature']), device('d2', ['temperature'])]);
    expect(many.triggers[0].deviceId).toBeUndefined();
    expect(many.triggers[0].capabilityId).toBe('');
  });

  it('carries the template seed (operator/threshold/action) into the draft', () => {
    const draft = buildDraftFromTemplate(antiFreeze, []);
    expect(draft.triggers[0].type).toBe('DeviceState');
    expect(draft.triggers[0].operator).toBe('lt');
    expect(draft.triggers[0].value).toBe(5);
    expect(draft.actions[0].type).toBe('Command');
    expect(draft.actions[0].set).toEqual({ on_off: true });
  });

  it('every template has a distinct id and a valid action kind', () => {
    const ids = SAFETY_TEMPLATES.map((t) => t.id);
    expect(new Set(ids).size).toBe(ids.length);
    for (const t of SAFETY_TEMPLATES) {
      expect(['Command', 'Notify']).toContain(t.actionKind);
      expect(t.triggerCapability).toBeTruthy();
    }
  });
});

describe('rule draft validity', () => {
  it('a blank draft is not saveable (no name, unbound trigger/action)', () => {
    expect(isDraftValid(emptyDraft())).toBe(false);
  });

  it('requires a name, a bound trigger and a complete action', () => {
    const d = emptyDraft();
    d.name = 'Test';
    d.triggers = [{ type: 'DeviceState', deviceId: 'd1', capabilityId: 'motion', operator: 'eq', value: true }];
    d.actions = [{ type: 'Command', deviceId: 'lamp', set: { on_off: true } }];
    expect(isDraftValid(d)).toBe(true);
  });

  it('validates every trigger and action (multi-trigger / multi-action)', () => {
    const d = emptyDraft();
    d.name = 'Multi';
    d.triggers = [
      { type: 'DeviceState', deviceId: 'd1', capabilityId: 'motion', operator: 'eq', value: true },
      { type: 'Sun', sun: 'Sunset', offsetMinutes: 0 },
    ];
    d.actions = [
      { type: 'Command', deviceId: 'lamp', set: { on_off: true } },
      { type: 'Notify', message: '' }, // incomplete → whole draft invalid
    ];
    expect(isDraftValid(d)).toBe(false);
    d.actions[1] = { type: 'Notify', message: 'Motion at dusk' };
    expect(isDraftValid(d)).toBe(true);
  });

  it('an unbound device-state trigger blocks saving', () => {
    const d = emptyDraft();
    d.name = 'Unbound';
    d.actions = [{ type: 'Notify', message: 'hi' }];
    // default trigger has no device/capability
    expect(isDraftValid(d)).toBe(false);
  });

  it('draftFromRule round-trips an existing rule and marks it for editing', () => {
    const rule: AutomationRule = {
      id: 'r1', name: 'Existing', status: 'Active', isProtected: false,
      triggers: [{ type: 'DeviceState', deviceId: 'd1', capabilityId: 'motion', operator: 'eq', value: true }],
      conditions: [{ type: 'Mode', mode: 'Night' }],
      actions: [{ type: 'Command', deviceId: 'lamp', set: { on_off: true } }],
      createdAt: '', updatedAt: '',
    };
    const d = draftFromRule(rule);
    expect(d.id).toBe('r1');
    expect(d.triggers).toHaveLength(1);
    expect(d.conditions[0]).toMatchObject({ type: 'Mode', mode: 'Night' });
    expect(isDraftValid(d)).toBe(true);
    // per-item clone: editing the draft must not mutate the source rule
    d.triggers[0].capabilityId = 'temperature';
    expect(rule.triggers[0].capabilityId).toBe('motion');
  });
});
