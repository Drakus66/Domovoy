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
  algorithm?: string | null;
}

export interface TrainResult {
  trained: boolean;
  message: string;
  model?: MlModel | null;
}

export const mlApi = {
  /** List registered models, newest first. */
  getModels: (): Promise<MlModel[]> =>
    apiClient.get<MlModel[]>('/api/ml/models').then((r) => r.data),

  /** Trigger a training run now (trains on recent telemetry, registers + reloads the model). */
  train: (): Promise<TrainResult> =>
    apiClient.post<TrainResult>('/api/ml/train').then((r) => r.data),
};
