// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

export type RuleStatus = 'Proposed' | 'Approved' | 'Active' | 'Disabled' | 'Shadow' | 'BoundedActive';
export type TriggerType = 'DeviceState' | 'Time' | 'Sun';
export type ConditionType = 'DeviceState' | 'TimeOfDay' | 'Sun' | 'Mode';
export type ActionType = 'Command' | 'Delay' | 'Notify' | 'Scene' | 'WaitForEvent';
export type SunEvent = 'Sunrise' | 'Sunset';

export interface RuleTrigger {
  type: TriggerType;
  deviceId?: string | null;
  zoneId?: string | null;
  capabilityId?: string | null;
  operator?: string | null;
  value?: unknown;
  cron?: string | null;
  sun?: SunEvent | null;
  offsetMinutes?: number;
}

export interface RuleCondition {
  type: ConditionType;
  deviceId?: string | null;
  zoneId?: string | null;
  capabilityId?: string | null;
  operator?: string | null;
  value?: unknown;
  fromTime?: string | null;
  toTime?: string | null;
  dark?: boolean | null;
  mode?: string | null;
}

export interface RuleAction {
  type: ActionType;
  deviceId?: string | null;
  set?: Record<string, unknown> | null;
  delaySeconds?: number;
  message?: string | null;
  /** Scene action (Epic 3B): id of the scene to activate. */
  sceneId?: string | null;
  /** WaitForEvent (Epic 3E) — same device-state match shape as RuleTrigger/RuleCondition. */
  waitDeviceId?: string | null;
  waitZoneId?: string | null;
  waitCapabilityId?: string | null;
  /** eq | ne | gt | lt | gte | lte (default eq). */
  waitOperator?: string | null;
  waitValue?: unknown;
  /** Seconds to wait before giving up (<=0 defaults to 300s server-side). */
  timeoutSeconds?: number;
  /** Branch: runs only when the wait timed out. Terminal — the sequence does not resume after it. */
  onTimeout?: RuleAction[] | null;
  /** Branch: runs if this action throws at runtime. Terminal. Notify.message may use the "{error}" token. */
  onError?: RuleAction[] | null;
}

/** Live gate (Epic 3E, "required expression"): must hold to start AND continuously while a rule runs. */
export interface RequiredExpression {
  conditions: RuleCondition[];
  /** e.g. "C0 && (C1 || !C2)"; blank = AND of all conditions. */
  expression: string;
}

/** Automation rule (matches Domovoy.Contracts AutomationRule, Epic 1A). */
export interface AutomationRule {
  id: string;
  name: string;
  description?: string | null;
  status: RuleStatus;
  isProtected: boolean;
  triggers: RuleTrigger[];
  conditions: RuleCondition[];
  /** Live gate (Epic 3E); null/absent = no gate beyond `conditions`. */
  requiredExpression?: RequiredExpression | null;
  actions: RuleAction[];
  createdAt: string;
  updatedAt: string;
}

/** A single rule run (matches DbGateway AutoHistory). */
export interface AutoHistoryEntry {
  timestamp: string;
  ruleId: string;
  ruleName: string;
  conditionsMet: boolean;
  success: boolean;
  triggerSummary: string;
  actionsExecuted: number;
  detail?: string | null;
}

export type NewRule = Omit<AutomationRule, 'id' | 'isProtected' | 'createdAt' | 'updatedAt'>;

export const automationsApi = {
  getRules: (): Promise<AutomationRule[]> =>
    apiClient.get<AutomationRule[]>('/api/automations').then((r) => r.data),

  getRule: (id: string): Promise<AutomationRule> =>
    apiClient.get<AutomationRule>(`/api/automations/${encodeURIComponent(id)}`).then((r) => r.data),

  createRule: (rule: NewRule): Promise<AutomationRule> =>
    apiClient.post<AutomationRule>('/api/automations', rule).then((r) => r.data),

  updateRule: (id: string, rule: AutomationRule): Promise<void> =>
    apiClient.put(`/api/automations/${encodeURIComponent(id)}`, rule).then(() => undefined),

  setStatus: (id: string, status: RuleStatus): Promise<void> =>
    apiClient.put(`/api/automations/${encodeURIComponent(id)}/status`, { status }).then(() => undefined),

  deleteRule: (id: string): Promise<void> =>
    apiClient.delete(`/api/automations/${encodeURIComponent(id)}`).then(() => undefined),

  getHistory: (ruleId?: string, limit = 100): Promise<AutoHistoryEntry[]> =>
    apiClient
      .get<AutoHistoryEntry[]>('/api/automations/history', { params: { ruleId, limit } })
      .then((r) => r.data),
};
