// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

export type ProposalKind = 'Rule' | 'BlockPromotion' | 'ModelSelection';
export type ProposalStatus = 'Proposed' | 'Approved' | 'Rejected';

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
  modelId?: string | null;
  metric?: string | null;
  score?: number | null;
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
};
