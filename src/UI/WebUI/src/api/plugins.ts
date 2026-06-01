import apiClient from './client';

export type PluginStatus =
  | 'Discovered' | 'Blocked' | 'Disabled' | 'Starting' | 'Running' | 'Stopped' | 'Failed';

export interface ResourceRequirements {
  cpuCores: number;
  memoryMb: number;
  gpu: boolean;
  internet: boolean;
}

/** A plugin's manifest + live supervision state (matches PluginSupervisor DTO, Epic 1C). */
export interface Plugin {
  id: string;
  name: string;
  version: string;
  description?: string | null;
  kind: string;
  providedCapabilities: string[];
  resources: ResourceRequirements;
  autoStart: boolean;
  status: PluginStatus;
  detail?: string | null;
  restartCount: number;
  lastStartedAt?: string | null;
  lastExitAt?: string | null;
}

/** Detected host resources the supervisor gates against. */
export interface HostResources {
  cpuCores: number;
  memoryMb: number;
  gpu: boolean;
  internet: boolean;
}

export interface PluginsResponse {
  host: HostResources;
  plugins: Plugin[];
}

export const pluginsApi = {
  getPlugins: (): Promise<PluginsResponse> =>
    apiClient.get<PluginsResponse>('/api/plugins').then((r) => r.data),

  start: (id: string): Promise<{ result: string }> =>
    apiClient.post(`/api/plugins/${encodeURIComponent(id)}/start`).then((r) => r.data),

  stop: (id: string): Promise<{ result: string }> =>
    apiClient.post(`/api/plugins/${encodeURIComponent(id)}/stop`).then((r) => r.data),
};
