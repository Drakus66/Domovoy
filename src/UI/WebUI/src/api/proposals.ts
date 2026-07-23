// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

export type ProposalKind = 'Rule' | 'BlockPromotion' | 'ModelSelection' | 'MlTask' | 'Scene' | 'RuleAmendment';
export type ProposalStatus = 'Proposed' | 'Approved' | 'Rejected';

/** A proposed scene to create on approve (matches Domovoy.Contracts Scene, Epic 3B). */
export interface SceneDraft {
  name: string;
  icon?: string | null;
  targets: { deviceId: string; set: Record<string, unknown> }[];
}

/** A candidate change awaiting approval (matches Domovoy.Contracts Proposal, Epic 2C). */
export interface Proposal {
  id: string;
  kind: ProposalKind;
  status: ProposalStatus;
  title: string;
  rationale?: string | null;
  source: string;
  ruleId?: string | null;
  blockId?: string | null;
  fromStage?: number | null;
  toStage?: number | null;
  modelVersion?: number | null;
  /** MlTask: target capability the proposed training task would learn (Epic 2P). */
  mlTaskTarget?: string | null;
  /** Scene: the scene to create on approve — devices + captured state (Epic 2F × 3B). */
  sceneDraft?: SceneDraft | null;
  /** Scene: optional daily cron; when set, approve also creates a rule that activates the new scene. */
  sceneScheduleCron?: string | null;
  /** RuleAmendment: what approve does to the rule ruleId points at — v1 "disable" (Epic 3J). */
  amendmentAction?: string | null;
  modelId?: string | null;
  metric?: string | null;
  score?: number | null;
  /** Structured numeric evidence behind the rationale (support, confidence, lift, mi, p, samples, windowDays, …). */
  evidence?: Record<string, number> | null;
  decisionId: string;
  createdAt: string;
  decidedAt?: string | null;
}

/** Outcome of a proposer scan (matches AutomationService RuleSuggester.SuggestResult). */
export interface SuggestResult {
  candidates: number;
  created: number;
  note: string;
}

/** Outcome of a discovery scan (matches AutomationService DiscoveryEngine.ScanResult, Epic 2F). */
export interface DiscoverResult {
  patterns: number;
  created: number;
  note: string;
}

/** Outcome of an ML-task suggestion scan (matches AutomationService MlTaskSuggester.ScanResult, Epic 2P). */
export interface SuggestTasksResult {
  candidates: number;
  created: number;
  note: string;
}

/** Fields the UI supplies when queuing a proposal; the server assigns id/status/decision. */
export type NewProposal = Pick<Proposal, 'kind' | 'title'> &
  Partial<Pick<Proposal, 'rationale' | 'source' | 'ruleId' | 'blockId' | 'fromStage' | 'toStage' | 'modelVersion' | 'modelId' | 'metric' | 'score'>>;

export const proposalsApi = {
  /** Queue, newest first; optional status filter (the UI defaults to Proposed). */
  list: (status?: ProposalStatus): Promise<Proposal[]> =>
    apiClient.get<Proposal[]>('/api/proposals', { params: { status } }).then((r) => r.data),

  /** Queue a proposal (e.g. a block-promotion or model-pin created from the Blocks page). */
  create: (proposal: NewProposal): Promise<Proposal> =>
    apiClient.post<Proposal>('/api/proposals', proposal).then((r) => r.data),

  /** Approve: applies the kind-specific side-effect and stamps a decision id (Epic 2C). */
  approve: (id: string): Promise<Proposal> =>
    apiClient.post<Proposal>(`/api/proposals/${encodeURIComponent(id)}/approve`).then((r) => r.data),

  /** Reject: leaves the target untouched. */
  reject: (id: string): Promise<void> =>
    apiClient.post(`/api/proposals/${encodeURIComponent(id)}/reject`).then(() => undefined),

  /** Run the heuristic proposer now — mines the event-log for candidate rules (Epic 2C, precursor to 2F). */
  suggest: (): Promise<SuggestResult> =>
    apiClient.post<SuggestResult>('/api/proposals/suggest').then((r) => r.data),

  /** Run the full pattern-discovery engine now — MI/FDR funnel over history (Epic 2F). */
  discover: (): Promise<DiscoverResult> =>
    apiClient.post<DiscoverResult>('/api/proposals/discover').then((r) => r.data),

  /** Scan for ML training-task candidates now — consumable targets with enough history (Epic 2P). */
  suggestTasks: (): Promise<SuggestTasksResult> =>
    apiClient.post<SuggestTasksResult>('/api/ml/suggest-tasks').then((r) => r.data),
};
