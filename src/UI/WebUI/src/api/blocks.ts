import apiClient from './client';

/** Binds a block input port to a source device capability (matches DbGateway PortBinding). */
export interface PortBinding {
  deviceId: string;
  capabilityId: string;
}

/** A control-block instance (matches Domovoy.Contracts ControlBlock, Epic 1H). */
export interface ControlBlock {
  id: string;
  name: string;
  typeId: string;
  deviceId: string; // virtual capability-device id (server-derived)
  zoneId?: string | null;
  enabled: boolean;
  params: Record<string, number>;
  inputs: Record<string, PortBinding>;
  outputs: Record<string, PortBinding>;
  createdAt: string;
  updatedAt: string;
}

export type NewBlock = Pick<ControlBlock, 'name' | 'typeId' | 'enabled' | 'params' | 'inputs' | 'outputs'> & {
  zoneId?: string | null;
};

/** Block-type schema from the catalog (drives the typed authoring form). */
export interface BlockCatalogEntry {
  typeId: string;
  title: string;
  description: string;
  inputs: { name: string; kind: string; description: string }[];
  outputs: { id: string; kind: string; unit?: string | null; writable: boolean }[];
  params: { name: string; default: number; unit?: string | null; min?: number | null; max?: number | null; description: string }[];
}

export const blocksApi = {
  getBlocks: (): Promise<ControlBlock[]> =>
    apiClient.get<ControlBlock[]>('/api/blocks').then((r) => r.data),

  getCatalog: (): Promise<BlockCatalogEntry[]> =>
    apiClient.get<BlockCatalogEntry[]>('/api/blocks/catalog').then((r) => r.data),

  createBlock: (block: NewBlock): Promise<ControlBlock> =>
    apiClient.post<ControlBlock>('/api/blocks', block).then((r) => r.data),

  deleteBlock: (id: string): Promise<void> =>
    apiClient.delete(`/api/blocks/${encodeURIComponent(id)}`).then(() => undefined),
};
