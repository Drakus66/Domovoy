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

/** Runtime health of one block (matches AutomationService BlockStatus, Epic 1H). */
export interface BlockStatus {
  blockId: string;
  enabled: boolean;
  lastTickAt: string | null;
  tickCount: number;
  lastEmittedCount: number;
  lastError: string | null;
  lastErrorAt: string | null;
}

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

  /** Live per-block runtime health (last-tick / error), keyed by block id. */
  getStatus: (): Promise<BlockStatus[]> =>
    apiClient.get<BlockStatus[]>('/api/blocks/status').then((r) => r.data),

  createBlock: (block: NewBlock): Promise<ControlBlock> =>
    apiClient.post<ControlBlock>('/api/blocks', block).then((r) => r.data),

  /** Update an existing block (name/params/bindings/enabled). The virtual device id stays stable. */
  updateBlock: (id: string, block: NewBlock): Promise<void> =>
    apiClient.put(`/api/blocks/${encodeURIComponent(id)}`, block).then(() => undefined),

  deleteBlock: (id: string): Promise<void> =>
    apiClient.delete(`/api/blocks/${encodeURIComponent(id)}`).then(() => undefined),
};
