// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { AutomationRule, RuleStatus } from '../api/automations';
import { CapabilityDevice } from '../api/capabilityDevices';

/**
 * Safety-rule templates (replacing the removed hardcoded "safety floor"): curated, *inert* starting
 * points for the kind of protective automation a home usually wants — anti-freeze, overheat, CO₂ →
 * ventilation, smoke → unlock, leak → shut-off. A template runs nothing on its own; the user picks one,
 * binds it to their own sensor/actuator in the rule builder and saves it as an ordinary automation. So
 * the household decides *what* is watched and *what* happens — the opposite of a rule wired into the code.
 */

export type ActionKind = 'Command' | 'Notify';

/** The rule-builder draft (shared with the Automations page's create dialog). */
export interface DraftState {
  name: string;
  trigDevice: string; trigCap: string; trigOp: string; trigValue: string;
  onlyDark: boolean;
  actionKind: ActionKind;
  actDevice: string; actCap: string; actValue: string;
  autoOffSeconds: number;
  notifyMessage: string;
  status: RuleStatus;
}

export const EMPTY_DRAFT: DraftState = {
  name: '', trigDevice: '', trigCap: '', trigOp: 'eq', trigValue: 'true',
  onlyDark: false, actionKind: 'Command',
  actDevice: '', actCap: '', actValue: 'true', autoOffSeconds: 0,
  notifyMessage: '', status: 'Active',
};

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
  /** Command (act on a device) or Notify (just alert). */
  actionKind: ActionKind;
  /** Structural draft fields the template pre-fills (device left blank for the user to bind). */
  seed: Partial<DraftState>;
}

/**
 * The shipped catalog. Capabilities use the open vocabulary (temperature/co2/humidity/smoke/water_leak);
 * a template only surfaces as "recommended" when the home actually has a matching sensor.
 */
export const SAFETY_TEMPLATES: SafetyTemplate[] = [
  {
    id: 'antiFreezeHeat',
    triggerCapability: 'temperature',
    actionKind: 'Command',
    seed: { trigOp: 'lt', trigValue: '5', actCap: 'on_off', actValue: 'true' },
  },
  {
    id: 'antiFreezeAlert',
    triggerCapability: 'temperature',
    actionKind: 'Notify',
    seed: { trigOp: 'lt', trigValue: '5' },
  },
  {
    id: 'overheatAlert',
    triggerCapability: 'temperature',
    actionKind: 'Notify',
    seed: { trigOp: 'gt', trigValue: '30' },
  },
  {
    id: 'co2Ventilation',
    triggerCapability: 'co2',
    actionKind: 'Command',
    seed: { trigOp: 'gt', trigValue: '1000', actCap: 'on_off', actValue: 'true' },
  },
  {
    id: 'co2Alert',
    triggerCapability: 'co2',
    actionKind: 'Notify',
    seed: { trigOp: 'gt', trigValue: '1400' },
  },
  {
    id: 'humidityVentilation',
    triggerCapability: 'humidity',
    actionKind: 'Command',
    seed: { trigOp: 'gt', trigValue: '70', actCap: 'on_off', actValue: 'true' },
  },
  {
    id: 'smokeUnlock',
    triggerCapability: 'smoke',
    actionKind: 'Command',
    seed: { trigOp: 'eq', trigValue: 'true', actCap: 'lock', actValue: 'false' },
  },
  {
    id: 'leakShutoff',
    triggerCapability: 'water_leak',
    actionKind: 'Command',
    seed: { trigOp: 'eq', trigValue: 'true', actCap: 'on_off', actValue: 'false' },
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
 * one device matches (unambiguous); otherwise the user picks it. Localized text (name/notify message) is
 * layered on by the caller after this returns.
 */
export const buildDraftFromTemplate = (
  template: SafetyTemplate, devices: CapabilityDevice[],
): DraftState => {
  const matches = devicesWithCapability(devices, template.triggerCapability);
  const trigDevice = matches.length === 1 ? matches[0].id : '';
  return {
    ...EMPTY_DRAFT,
    ...template.seed,
    actionKind: template.actionKind,
    trigDevice,
    trigCap: trigDevice ? template.triggerCapability : '',
  };
};
