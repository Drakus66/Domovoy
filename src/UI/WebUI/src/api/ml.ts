// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** Spatial scope a model serves (Epic 2I): a zone, a zone kind, or global. */
export interface ModelScope {
  level: string; // "zone" | "zone_kind" | "global"
  key: string;   // zone id / zone kind; empty for global
}

/** Registered ML model metadata (matches Domovoy.Contracts MlModel, Epic 2A/2I). */
export interface MlModel {
  id: string;
  name: string;
  kind: string;
  targetCapability: string;
  /** Spatial scope along the zone → zone_kind → global chain (Epic 2I). */
  scope?: ModelScope | null;
  version: number;
  trainedAt: string;
  sampleCount: number;
  rmse: number;
  /** Held-out backtest MAE (prediction vs fact on unseen recent data, Epic 2B). */
  holdoutMae: number;
  holdoutSampleCount: number;
  /** Honest holdout score in the template's metric (Epic 2I). */
  holdoutScore: number;
  /** Holdout metric name: MAE / AUC / MacroAccuracy (Epic 2I). */
  metric: string;
  /** Feature set the model was trained on, e.g. "time" (Epic 2I). */
  features: string;
  algorithm?: string | null;
}

/** Trainer-owned outcome of the latest training attempt of a task (Epic 2P). */
export interface MlTaskStatus {
  lastTrainAt?: string | null;
  lastTrainOk: boolean;
  lastMessage?: string | null;
  lastSampleCount: number;
  lastRegisteredScopes: number;
}

/** An ML training task (matches Domovoy.Contracts MlTask, Epic 2P): what/from what/within which limits to learn. */
export interface MlTask {
  id: string;
  name: string;
  targetCapability: string;
  enabled: boolean;
  windowDays: number;
  minSamples: number;
  trainIntervalHours: number;
  trainZoneModels: boolean;
  zonePromotionMargin: number;
  clampMin?: number | null;
  clampMax?: number | null;
  keepLastVersions: number;
  createdAt: string;
  updatedAt: string;
  status?: MlTaskStatus | null;
}

/** Editable subset of a task the create/edit wizard submits (Epic 2P). */
export type NewMlTask = Omit<MlTask, 'id' | 'createdAt' | 'updatedAt' | 'status'>;

export interface TrainResult {
  trained: boolean;
  message: string;
  model?: MlModel | null;
}

/** Per-task outcome of a train run (Epic 2P): POST /api/ml/train returns a list of these. */
export interface TaskTrainResult {
  taskId: string;
  target: string;
  result: TrainResult;
}

/** Sample availability of one scope for the data-sufficiency check (Epic 2P). */
export interface ScopeDataCheck {
  level: string;
  key: string;
  samples: number;
  required: number;
  sufficient: boolean;
}

/** "Will this train?" diagnostics for a (prospective) task (Epic 2P). */
export interface DataCheck {
  target: string;
  windowDays: number;
  minSamples: number;
  kind: string; // Number | Boolean | Enum
  templateAvailable: boolean;
  scopes: ScopeDataCheck[];
}

/** One backtest point: the serving model's prediction vs actual history (Epic 2B scorecard). */
export interface BacktestPoint {
  timestamp: string;
  predicted: number;
  actual: number;
}

export interface Backtest {
  model?: MlModel | null;
  points: BacktestPoint[];
  /** Enum targets: share of matching class predictions instead of a numeric series (Epic 2P). */
  hitRate?: number | null;
}

/** One device the ML archetype classifier would type differently than it currently is (Epic 2D). */
export interface ArchetypeDisagreement {
  deviceId: string;
  name: string;
  current: string;
  predicted: string;
  confidence: number;
}

/** Outcome of running the ML.NET archetype classifier over the device population (Epic 2D). */
export interface ArchetypeClassifyResult {
  trained: boolean;
  devices: number;
  trainedOn: number;
  disagreements: ArchetypeDisagreement[];
  note: string;
}

export const mlApi = {
  /** List registered models, newest first. */
  getModels: (): Promise<MlModel[]> =>
    apiClient.get<MlModel[]>('/api/ml/models').then((r) => r.data),

  /** Delete one model version (Epic 2P retention). */
  deleteModel: (id: string): Promise<void> =>
    apiClient.delete(`/api/ml/models/${encodeURIComponent(id)}`).then(() => undefined),

  /** Prune old versions per (kind, target, scope) line, keeping the N most recent (Epic 2P retention). */
  pruneModels: (keepLast: number, target?: string): Promise<{ deleted: number }> =>
    apiClient.post<{ deleted: number }>('/api/ml/models/prune', { keepLast, target }).then((r) => r.data),

  // ===== ML tasks (Epic 2P) =====

  getTasks: (): Promise<MlTask[]> =>
    apiClient.get<MlTask[]>('/api/ml/tasks').then((r) => r.data),

  createTask: (task: NewMlTask): Promise<MlTask> =>
    apiClient.post<MlTask>('/api/ml/tasks', task).then((r) => r.data),

  updateTask: (id: string, task: NewMlTask): Promise<MlTask> =>
    apiClient.put<MlTask>(`/api/ml/tasks/${encodeURIComponent(id)}`, task).then((r) => r.data),

  deleteTask: (id: string): Promise<void> =>
    apiClient.delete(`/api/ml/tasks/${encodeURIComponent(id)}`).then(() => undefined),

  /** Instant "will this train?" check for a (prospective) task (Epic 2P). */
  dataCheck: (target: string, windowDays: number, minSamples: number, zones = true): Promise<DataCheck> =>
    apiClient
      .get<DataCheck>(
        `/api/ml/data-check?target=${encodeURIComponent(target)}&windowDays=${windowDays}` +
          `&minSamples=${minSamples}&zones=${zones}`,
      )
      .then((r) => r.data),

  /** Trigger a training run now: for one task or (without taskId) every enabled task. */
  train: (taskId?: string): Promise<TaskTrainResult[]> =>
    apiClient
      .post<TaskTrainResult[]>(`/api/ml/train${taskId ? `?taskId=${encodeURIComponent(taskId)}` : ''}`)
      .then((r) => r.data),

  /** Backtest scorecard: the serving model of (target, scope) vs actual history over the last `days`. */
  backtest: (days = 7, target?: string, level?: string, key?: string): Promise<Backtest> => {
    let url = `/api/ml/backtest?days=${days}`;
    if (target) url += `&target=${encodeURIComponent(target)}`;
    if (level) url += `&level=${encodeURIComponent(level)}`;
    if (key) url += `&key=${encodeURIComponent(key)}`;
    return apiClient.get<Backtest>(url).then((r) => r.data);
  },

  /** Run the ML.NET archetype classifier over the device population; returns disagreements (Epic 2D). */
  classifyArchetypes: (): Promise<ArchetypeClassifyResult> =>
    apiClient.post<ArchetypeClassifyResult>('/api/ml/classify-archetypes').then((r) => r.data),
};
