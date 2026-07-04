import apiClient from './client';

/** Current home mode / presence context (matches DbGateway HomeState, roadmap Epic 1G). */
export interface HomeState {
  mode: string;
  source: string;
  updatedAt: string;
}

export const modeApi = {
  getMode: (): Promise<HomeState> =>
    apiClient.get<HomeState>('/api/mode').then((r) => r.data),

  /** Well-known modes for selection (open set; deployments may add their own). */
  getOptions: (): Promise<string[]> =>
    apiClient.get<string[]>('/api/mode/options').then((r) => r.data),

  /** Manual switch. The gateway persists it and broadcasts the change to the engine + event-log. */
  setMode: (mode: string): Promise<HomeState> =>
    apiClient.put<HomeState>('/api/mode', { mode, source: 'user' }).then((r) => r.data),
};
