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
  /** True when the plugin announced a settings schema — drives the settings button on the panel. */
  hasSettings: boolean;
}

/** One configurable plugin setting (mirrors PluginSettingDescriptor; the shape the dialog renders). */
export interface PluginSetting {
  key: string;
  kind: 'boolean' | 'number' | 'enum' | 'text' | 'secret' | string;
  label?: string | null;
  description?: string | null;
  default?: unknown;
  min?: number | null;
  max?: number | null;
  step?: number | null;
  unit?: string | null;
  values?: string[] | null;
  editor?: string | null;
  secret?: boolean;
}

/** Schema + current values for a plugin's settings (secrets are returned masked/blank). */
export interface PluginSettingsResponse {
  settings: PluginSetting[];
  values: Record<string, unknown>;
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

/** Response of a plugin install: the outcome message and, on success, the freshly registered plugin. */
export interface InstallResponse {
  result: string;
  plugin?: Plugin | null;
}

export const pluginsApi = {
  getPlugins: (): Promise<PluginsResponse> =>
    apiClient.get<PluginsResponse>('/api/plugins').then((r) => r.data),

  start: (id: string): Promise<{ result: string }> =>
    apiClient.post(`/api/plugins/${encodeURIComponent(id)}/start`).then((r) => r.data),

  stop: (id: string): Promise<{ result: string }> =>
    apiClient.post(`/api/plugins/${encodeURIComponent(id)}/stop`).then((r) => r.data),

  /** Upload a plugin package (.zip with plugin.json + binaries); the supervisor extracts & auto-starts it. */
  install: (file: File): Promise<InstallResponse> => {
    const form = new FormData();
    form.append('package', file);
    return apiClient
      .post<InstallResponse>('/api/plugins/install', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      .then((r) => r.data);
  },

  uninstall: (id: string): Promise<{ result: string }> =>
    apiClient.delete(`/api/plugins/${encodeURIComponent(id)}`).then((r) => r.data),

  /** Schema + current values of a plugin's settings (secrets masked). */
  getSettings: (id: string): Promise<PluginSettingsResponse> =>
    apiClient.get<PluginSettingsResponse>(`/api/plugins/${encodeURIComponent(id)}/settings`).then((r) => r.data),

  /** Save settings; the supervisor persists them and broadcasts them to the plugin (applied live). */
  updateSettings: (id: string, values: Record<string, unknown>): Promise<{ result: string }> =>
    apiClient.put(`/api/plugins/${encodeURIComponent(id)}/settings`, { values }).then((r) => r.data),
};
