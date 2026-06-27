import apiClient from './client';

/** Registered ML model metadata (matches Domovoy.Contracts MlModel, Epic 2A). */
export interface MlModel {
  id: string;
  name: string;
  kind: string;
  targetCapability: string;
  version: number;
  trainedAt: string;
  sampleCount: number;
  rmse: number;
  /** Held-out backtest MAE (prediction vs fact on unseen recent data, Epic 2B). */
  holdoutMae: number;
  holdoutSampleCount: number;
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
};
