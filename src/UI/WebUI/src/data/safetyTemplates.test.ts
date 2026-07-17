// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import {
  SAFETY_TEMPLATES, SafetyTemplate, buildDraftFromTemplate, isTemplateRecommended,
  recommendedTemplateIds,
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
    expect(one.trigDevice).toBe('d1');
    expect(one.trigCap).toBe('temperature');

    const many = buildDraftFromTemplate(antiFreeze, [device('d1', ['temperature']), device('d2', ['temperature'])]);
    expect(many.trigDevice).toBe('');
    expect(many.trigCap).toBe('');
  });

  it('carries the template seed (operator/threshold/action) into the draft', () => {
    const draft = buildDraftFromTemplate(antiFreeze, []);
    expect(draft.trigOp).toBe('lt');
    expect(draft.trigValue).toBe('5');
    expect(draft.actionKind).toBe('Command');
    expect(draft.actCap).toBe('on_off');
    expect(draft.actValue).toBe('true');
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
