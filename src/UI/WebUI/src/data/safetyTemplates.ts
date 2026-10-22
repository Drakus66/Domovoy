// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { AutomationRule, RuleStatus, RuleTrigger, RuleCondition, RuleAction, RequiredExpression } from '../api/automations';
import { CapabilityDevice } from '../api/capabilityDevices';

/**
 * The rule-builder draft — the editable shape behind the Automations create/edit dialog. It mirrors the
 * server rule model (Domovoy.Contracts AutomationRule): a rule fires when ANY trigger matches (OR), runs
 * only while ALL conditions hold (AND, empty = always), and executes its actions in order. Holding the
 * same arrays the API expects means the dialog can author several triggers, conditions and actions without
 * a lossy flat intermediate. `id` is set only when editing an existing rule.
 */
export interface RuleDraft {
  id?: string;
  name: string;
  status: RuleStatus;
  triggers: RuleTrigger[];
  conditions: RuleCondition[];
  /** Live gate (Epic 3E); undefined/null = no gate beyond `conditions`. */
  requiredExpression?: RequiredExpression | null;
  actions: RuleAction[];
}

/** A fresh, unbound device-state trigger (the default row when adding a trigger). */
export const newTrigger = (): RuleTrigger => ({ type: 'DeviceState', operator: 'eq', value: true });
/** A fresh mode condition (a safe, always-complete default when adding a condition). */
export const newCondition = (): RuleCondition => ({ type: 'Mode', mode: 'Home' });
/** A fresh, unbound command action (the default row when adding an action). */
export const newAction = (): RuleAction => ({ type: 'Command', set: {} });

/** A blank draft: one trigger and one action to fill in, no conditions. Fresh arrays every call. */
export const emptyDraft = (): RuleDraft => ({
  name: '', status: 'Active', triggers: [newTrigger()], conditions: [], requiredExpression: undefined, actions: [newAction()],
});

/** Editable copy of an existing rule for the edit path — per-item clones so page state is never mutated. */
export const draftFromRule = (rule: AutomationRule): RuleDraft => ({
  id: rule.id,
  name: rule.name,
  status: rule.status,
  triggers: rule.triggers.length ? rule.triggers.map((t) => ({ ...t })) : [newTrigger()],
  conditions: (rule.conditions ?? []).map((c) => ({ ...c })),
  requiredExpression: rule.requiredExpression
    ? { expression: rule.requiredExpression.expression, conditions: rule.requiredExpression.conditions.map((c) => ({ ...c })) }
    : undefined,
  actions: rule.actions.length ? rule.actions.map((a) => ({ ...a })) : [newAction()],
});

/**
 * A trigger is bound when it can actually fire: DeviceState needs a capability plus a target — either a
 * specific device or a whole zone (Epic 3G: a zone-scoped match fires on any device in the zone, honoured
 * by the backend RuleEvaluator); Time needs a cron; Sun always can.
 */
const triggerBound = (t: RuleTrigger): boolean => {
  if (t.type === 'DeviceState') return (!!t.deviceId || !!t.zoneId) && !!t.capabilityId;
  if (t.type === 'Time') return !!t.cron?.trim();
  return true; // Sun
};

/**
 * An action is complete when runnable: Command needs a device + one set assignment, Notify a message,
 * Delay a positive wait, Scene a scene id, WaitForEvent a capability + a device or zone to watch (timeout
 * and branches are optional — a wait defaults to 300s server-side and simply falls through with no branch).
 */
const actionComplete = (a: RuleAction): boolean => {
  if (a.type === 'Command') {
    const keys = Object.keys(a.set ?? {});
    return !!a.deviceId && keys.length > 0 && keys[0] !== '';
  }
  if (a.type === 'Notify') return !!a.message?.trim();
  if (a.type === 'Delay') return (a.delaySeconds ?? 0) > 0;
  if (a.type === 'Scene') return !!a.sceneId;
  if (a.type === 'WaitForEvent') return !!a.waitCapabilityId && !!(a.waitDeviceId || a.waitZoneId);
  return false;
};

/** A condition is complete when evaluable: DeviceState needs a capability + a device or zone (Epic 3G), TimeOfDay a window; Sun/Mode always. */
const conditionComplete = (c: RuleCondition): boolean => {
  if (c.type === 'DeviceState') return (!!c.deviceId || !!c.zoneId) && !!c.capabilityId;
  if (c.type === 'TimeOfDay') return !!c.fromTime && !!c.toTime;
  return true; // Sun, Mode
};

/** A draft is saveable when named, every trigger is bound, every action complete, and any conditions complete. */
export const isDraftValid = (d: RuleDraft): boolean =>
  !!d.name.trim() &&
  d.triggers.length > 0 && d.triggers.every(triggerBound) &&
  d.actions.length > 0 && d.actions.every(actionComplete) &&
  d.conditions.every(conditionComplete);

/**
 * Safety-rule templates (replacing the removed hardcoded "safety floor"): curated, *inert* starting
 * points for the kind of protective automation a home usually wants — anti-freeze, overheat, CO₂ →
 * ventilation, smoke → unlock, leak → shut-off. A template runs nothing on its own; the user picks one,
 * binds it to their own sensor/actuator in the rule builder and saves it as an ordinary automation. So
 * the household decides *what* is watched and *what* happens — the opposite of a rule wired into the code.
 */
export type ActionKind = 'Command' | 'Notify';

/**
 * A safety template = the structural seed of a rule. Text (title/description/notify message) is NOT held
 * here — it is resolved from i18n by <c>templates.items.&lt;id&gt;.*</c> so the catalog stays localizable and
 * this module stays a pure, testable data/logic unit.
 */
export interface SafetyTemplate {
  /** Stable id; also the i18n key suffix (templates.items.&lt;id&gt;.title/.desc/.message). */
  id: string;
  /** Sensor capability the rule watches — drives the "recommended" match and pre-fills the trigger. */
  triggerCapability: string;
  /** Comparison + threshold the trigger fires on (e.g. lt 5, gt 1000). */
  triggerOperator: string;
  triggerValue: unknown;
  /** Command (act on a device) or Notify (just alert). */
  actionKind: ActionKind;
  /** For Command templates: the writable capability to set and the value to set it to. */
  actionCapability?: string;
  actionValue?: unknown;
}

/**
 * The shipped catalog. Capabilities use the open vocabulary (temperature/co2/humidity/smoke/water_leak);
 * a template only surfaces as "recommended" when the home actually has a matching sensor.
 */
export const SAFETY_TEMPLATES: SafetyTemplate[] = [
  {
    id: 'antiFreezeHeat', triggerCapability: 'temperature', triggerOperator: 'lt', triggerValue: 5,
    actionKind: 'Command', actionCapability: 'on_off', actionValue: true,
  },
  {
    id: 'antiFreezeAlert', triggerCapability: 'temperature', triggerOperator: 'lt', triggerValue: 5,
    actionKind: 'Notify',
  },
  {
    id: 'overheatAlert', triggerCapability: 'temperature', triggerOperator: 'gt', triggerValue: 30,
    actionKind: 'Notify',
  },
  {
    id: 'co2Ventilation', triggerCapability: 'co2', triggerOperator: 'gt', triggerValue: 1000,
    actionKind: 'Command', actionCapability: 'on_off', actionValue: true,
  },
  {
    id: 'co2Alert', triggerCapability: 'co2', triggerOperator: 'gt', triggerValue: 1400,
    actionKind: 'Notify',
  },
  {
    id: 'humidityVentilation', triggerCapability: 'humidity', triggerOperator: 'gt', triggerValue: 70,
    actionKind: 'Command', actionCapability: 'on_off', actionValue: true,
  },
  {
    id: 'smokeUnlock', triggerCapability: 'smoke', triggerOperator: 'eq', triggerValue: true,
    actionKind: 'Command', actionCapability: 'lock', actionValue: false,
  },
  {
    id: 'leakShutoff', triggerCapability: 'water_leak', triggerOperator: 'eq', triggerValue: true,
    actionKind: 'Command', actionCapability: 'on_off', actionValue: false,
  },
];

const deviceHasCapability = (d: CapabilityDevice, cap: string): boolean =>
  d.capabilities.some((c) => c.id === cap);

/** Devices exposing a given capability (the candidate sensors a template could bind to). */
export const devicesWithCapability = (devices: CapabilityDevice[], cap: string): CapabilityDevice[] =>
  devices.filter((d) => deviceHasCapability(d, cap));

/** True when some existing rule already watches this capability in a DeviceState trigger. */
const capabilityCovered = (rules: AutomationRule[], cap: string): boolean =>
  rules.some((r) => r.triggers.some((tr) => tr.type === 'DeviceState' && tr.capabilityId === cap));

/**
 * A template is "recommended" when the home has a device exposing its trigger capability but no existing
 * automation already watches that capability — i.e. a protective gap the user could plausibly want closed.
 */
export const isTemplateRecommended = (
  template: SafetyTemplate, devices: CapabilityDevice[], rules: AutomationRule[],
): boolean =>
  devicesWithCapability(devices, template.triggerCapability).length > 0 &&
  !capabilityCovered(rules, template.triggerCapability);

/** Ids of the templates worth surfacing first, given the current devices/rules. */
export const recommendedTemplateIds = (
  templates: SafetyTemplate[], devices: CapabilityDevice[], rules: AutomationRule[],
): Set<string> =>
  new Set(templates.filter((tpl) => isTemplateRecommended(tpl, devices, rules)).map((tpl) => tpl.id));

/**
 * Build a pre-filled builder draft from a template. The trigger device is auto-selected only when exactly
 * one device matches (unambiguous); otherwise the user picks it (and the capability with it). Localized
 * text (name/notify message) is layered on by the caller after this returns.
 */
export const buildDraftFromTemplate = (
  template: SafetyTemplate, devices: CapabilityDevice[],
): RuleDraft => {
  const matches = devicesWithCapability(devices, template.triggerCapability);
  const trigDevice = matches.length === 1 ? matches[0].id : undefined;
  const trigger: RuleTrigger = {
    type: 'DeviceState',
    deviceId: trigDevice,
    capabilityId: trigDevice ? template.triggerCapability : '',
    operator: template.triggerOperator,
    value: template.triggerValue,
  };
  const action: RuleAction = template.actionKind === 'Notify'
    ? { type: 'Notify', message: '' }
    : { type: 'Command', deviceId: '', set: { [template.actionCapability as string]: template.actionValue } };
  return { name: '', status: 'Active', triggers: [trigger], conditions: [], actions: [action] };
};
