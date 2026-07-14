// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Localized rendering of proposals (Epic 2C). The server stores English title/rationale as the stable
// dedup key; the UI rebuilds the user-facing text in the active language — the rule sentence from the
// referenced AutomationRule itself, the justification from the structured `evidence` numbers. The raw
// server strings remain the fallback when a target has been deleted or a proposal predates `evidence`.

import i18n from 'i18next';
import { Proposal } from '../../api/proposals';
import { AutomationRule, RuleAction, RuleCondition, RuleTrigger } from '../../api/automations';
import { capabilityLabel } from '../devices/deviceVisuals';

const t = (key: string, options?: Record<string, unknown>) => i18n.t(`proposals:${key}`, options ?? {});

/** Resolves a device id to its display name (falls back to the id). */
export type DeviceNameOf = (deviceId?: string | null) => string;

const KNOWN_SOURCES = ['user', 'ml_proposer', 'discovery', 'ml_task_scanner', 'demo'];

/** Normalized key for the source chip + tooltip ('other' for anything unknown). */
export const sourceKey = (source: string): string =>
  KNOWN_SOURCES.includes(source) ? source : 'other';

export const stageName = (s?: number | null): string =>
  t(`stageName.${s === 2 ? 'full' : s === 1 ? 'bounded' : 'shadow'}`);

const fmtValue = (v: unknown): string =>
  v === true ? t('rule.on') : v === false ? t('rule.off') : v == null ? '—' : String(v);

const opSymbol = (op?: string | null): string =>
  op === 'lt' ? '<' : op === 'gt' ? '>' : op === 'lte' ? '≤' : op === 'gte' ? '≥' : op === 'ne' ? '≠' : '=';

// The miners emit plain daily crons ("M H * * *"); render those as a time of day, anything else raw.
const cronText = (cron?: string | null): string => {
  const m = /^(\d{1,2}) (\d{1,2}) \* \* \*$/.exec(cron ?? '');
  if (m) return t('rule.daily', { time: `${m[2].padStart(2, '0')}:${m[1].padStart(2, '0')}` });
  return t('rule.cron', { cron: cron ?? '' });
};

const triggerText = (tr: RuleTrigger, nameOf: DeviceNameOf): string => {
  if (tr.type === 'Time') return cronText(tr.cron);
  if (tr.type === 'Sun') {
    const event = t(tr.sun === 'Sunrise' ? 'rule.sunrise' : 'rule.sunset');
    return tr.offsetMinutes ? t('rule.offset', { event, minutes: tr.offsetMinutes }) : event;
  }
  const device = nameOf(tr.deviceId);
  const capability = capabilityLabel(tr.capabilityId ?? '');
  if ((tr.operator ?? 'eq') === 'eq' && tr.value === true)
    return t('rule.stateActive', { device, capability });
  return t('rule.state', { device, capability, op: opSymbol(tr.operator), value: fmtValue(tr.value) });
};

const conditionText = (c: RuleCondition, nameOf: DeviceNameOf): string => {
  if (c.type === 'TimeOfDay') return t('rule.timeWindow', { from: c.fromTime ?? '', to: c.toTime ?? '' });
  if (c.type === 'Mode') return t('rule.mode', { mode: c.mode ?? '' });
  if (c.type === 'Sun') return t(c.dark === false ? 'rule.whileLight' : 'rule.whileDark');
  return t('rule.state', {
    device: nameOf(c.deviceId),
    capability: capabilityLabel(c.capabilityId ?? ''),
    op: opSymbol(c.operator),
    value: fmtValue(c.value),
  });
};

const actionText = (a: RuleAction, nameOf: DeviceNameOf): string => {
  if (a.type === 'Delay') return t('rule.wait', { seconds: a.delaySeconds ?? 0 });
  if (a.type === 'Notify') return t('rule.notify', { message: a.message ?? '' });
  const device = nameOf(a.deviceId);
  const entries = Object.entries(a.set ?? {});
  if (entries.length === 1 && entries[0][0] === 'on_off')
    return t(entries[0][1] === true ? 'rule.turnOn' : 'rule.turnOff', { device });
  return entries
    .map(([capId, v]) => t('rule.set', { device, capability: capabilityLabel(capId), value: fmtValue(v) }))
    .join(', ');
};

/** Localized one-line "If … — do …" summary of a rule, built from its triggers/conditions/actions. */
export const describeRule = (rule: AutomationRule, nameOf: DeviceNameOf): string => {
  const when = [
    ...rule.triggers.map((tr) => triggerText(tr, nameOf)),
    ...rule.conditions.map((c) => conditionText(c, nameOf)),
  ].join(', ');
  const then = rule.actions.map((a) => actionText(a, nameOf)).join(', ');
  return t('rule.sentence', { when, then });
};

/** Localized card headline; falls back to the server-generated (English) title. */
export const proposalTitle = (
  p: Proposal, rule: AutomationRule | undefined, nameOf: DeviceNameOf,
): string => {
  if (p.kind === 'Rule' && rule) return describeRule(rule, nameOf);
  if (p.kind === 'MlTask' && p.mlTaskTarget)
    return t('mlTaskTitle', { target: capabilityLabel(p.mlTaskTarget) });
  return p.title;
};

/** Localized "what approve will do" line — the concrete side-effect of this proposal's kind. */
export const effectText = (p: Proposal): string => {
  switch (p.kind) {
    case 'Rule':
      return t('effect.rule');
    case 'BlockPromotion':
      return t('effect.promotion', { from: stageName(p.fromStage), to: stageName(p.toStage) });
    case 'ModelSelection':
      return p.modelVersion ? t('effect.pin', { version: p.modelVersion }) : t('effect.unpin');
    case 'MlTask':
      return t('effect.mlTask', { target: capabilityLabel(p.mlTaskTarget ?? '') });
    default:
      return '';
  }
};

/** Localized justification from the structured evidence; falls back to the raw server rationale. */
export const evidenceText = (p: Proposal): string | null => {
  const e = p.evidence;
  if (!e) return p.rationale ?? null;

  if (p.kind === 'MlTask' && e.samples !== undefined)
    return t('evidence.mlTask', { samples: e.samples, required: e.required ?? 0, windowDays: e.windowDays ?? 0 });

  // Setpoint-preference discovery (Epic 2F type B) carries the learned value + spread.
  if (e.value !== undefined && e.stdDev !== undefined)
    return t('evidence.setpoint', { value: e.value, support: e.support ?? 0, stdDev: e.stdDev });

  if (e.support !== undefined && e.confidence !== undefined) {
    const base = t('evidence.coocc', {
      support: e.support,
      windowDays: e.windowDays ?? 0,
      confidence: Math.round(e.confidence * 100),
    });
    const stats = e.lift !== undefined
      ? ' ' + t('evidence.stats', { lift: e.lift.toFixed(1), p: (e.p ?? 0).toFixed(3) })
      : '';
    return base + stats;
  }

  return p.rationale ?? null;
};
