import apiClient from './client';

/** Area/zone of the home and grounds (matches DbGateway Zone model, roadmap P0-3). */
export interface Zone {
  id: string;
  name: string;
  description?: string | null;
  parentZoneId?: string | null;
  kind?: string | null;
  order: number;
  createdAt: string;
  updatedAt: string;
}

/** Create/update payload (id is server/route assigned). */
export interface ZoneInput {
  name: string;
  description?: string | null;
  parentZoneId?: string | null;
  kind?: string | null;
  order?: number;
}

export const zonesApi = {
  getZones: (): Promise<Zone[]> =>
    apiClient.get<Zone[]>('/api/zones').then((r) => r.data),

  createZone: (input: ZoneInput): Promise<Zone> =>
    apiClient.post<Zone>('/api/zones', input).then((r) => r.data),

  updateZone: (id: string, input: ZoneInput): Promise<void> =>
    apiClient.put(`/api/zones/${encodeURIComponent(id)}`, input).then(() => undefined),

  deleteZone: (id: string): Promise<void> =>
    apiClient.delete(`/api/zones/${encodeURIComponent(id)}`).then(() => undefined),
};
