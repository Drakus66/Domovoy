import apiClient from './client';

/**
 * Custom dashboard tabs on the main page (custom dashboards epic). Matches the DbGateway
 * DashboardDocument model. One household — no per-user scoping until auth lands in Phase 3.
 */
export type DashboardItemType = 'device' | 'capability' | 'chart' | 'modes';

export interface DashboardItem {
  type: DashboardItemType;
  deviceId?: string | null;
  /** Capability id for `capability`/`chart` items. */
  capabilityId?: string | null;
  /** Per-type extras (chart: hours, bucket). */
  params?: Record<string, unknown> | null;
}

export interface DashboardSection {
  title: string;
  items: DashboardItem[];
}

export interface Dashboard {
  id: string;
  name: string;
  icon?: string | null;
  order: number;
  sections: DashboardSection[];
  createdAt: string;
  updatedAt: string;
}

/** Create/update payload (id/order are server assigned). */
export interface DashboardInput {
  name: string;
  icon?: string | null;
  sections: DashboardSection[];
}

/** Household-wide preferences: which auto-sphere tabs are hidden. */
export interface DashboardPrefs {
  id: string;
  hiddenSpheres: string[];
  updatedAt: string;
}

export const dashboardsApi = {
  list: (): Promise<Dashboard[]> =>
    apiClient.get<Dashboard[]>('/api/dashboards').then((r) => r.data),

  create: (input: DashboardInput): Promise<Dashboard> =>
    apiClient.post<Dashboard>('/api/dashboards', input).then((r) => r.data),

  update: (id: string, input: DashboardInput): Promise<void> =>
    apiClient.put(`/api/dashboards/${encodeURIComponent(id)}`, input).then(() => undefined),

  remove: (id: string): Promise<void> =>
    apiClient.delete(`/api/dashboards/${encodeURIComponent(id)}`).then(() => undefined),

  reorder: (ids: string[]): Promise<void> =>
    apiClient.put('/api/dashboards/order', { ids }).then(() => undefined),

  getPrefs: (): Promise<DashboardPrefs> =>
    apiClient.get<DashboardPrefs>('/api/dashboards/prefs').then((r) => r.data),

  savePrefs: (hiddenSpheres: string[]): Promise<DashboardPrefs> =>
    apiClient.put<DashboardPrefs>('/api/dashboards/prefs', { hiddenSpheres }).then((r) => r.data),
};
