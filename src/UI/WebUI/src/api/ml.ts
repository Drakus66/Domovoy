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

export interface TrainResult {
  trained: boolean;
  message: string;
  model?: MlModel | null;
}

/** One backtest point: the loaded model's prediction vs actual telemetry (Epic 2B scorecard). */
export interface BacktestPoint {
  timestamp: string;
  predicted: number;
  actual: number;
}

export interface Backtest {
  model?: MlModel | null;
  points: BacktestPoint[];
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

  /** Trigger a training run now (trains on recent telemetry, registers + reloads the model). */
  train: (): Promise<TrainResult> =>
    apiClient.post<TrainResult>('/api/ml/train').then((r) => r.data),

  /** Backtest scorecard: the loaded model's prediction vs actual telemetry over the last `days`. */
  backtest: (days = 7): Promise<Backtest> =>
    apiClient.get<Backtest>(`/api/ml/backtest?days=${days}`).then((r) => r.data),

  /** Run the ML.NET archetype classifier over the device population; returns disagreements (Epic 2D). */
  classifyArchetypes: (): Promise<ArchetypeClassifyResult> =>
    apiClient.post<ArchetypeClassifyResult>('/api/ml/classify-archetypes').then((r) => r.data),
};
