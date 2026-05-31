import apiClient from './client';

export type CapabilityKind = 'Boolean' | 'Number' | 'Enum' | 'Color' | 'Text' | 'Action';

/** A single capability of a device (matches DbGateway CapabilityDocument). */
export interface Capability {
  id: string;
  kind: CapabilityKind | string;
  writable: boolean;
  unit?: string | null;
  min?: number | null;
  max?: number | null;
}

/** Capability device read-model (matches DbGateway CapabilityDeviceDocument). */
export interface CapabilityDevice {
  id: string;
  name: string;
  adapterSource: string;
  model?: string | null;
  zoneId: string;
  capabilities: Capability[];
  state: Record<string, unknown>;
  isOnline: boolean;
  lastUpdated: string;
}

export const capabilityDevicesApi = {
  /** List capability devices (Zigbee, native and emulator devices, normalized). */
  getDevices: (): Promise<CapabilityDevice[]> =>
    apiClient.get<CapabilityDevice[]>('/api/capability-devices').then((r) => r.data),

  /**
   * Send a capability-addressed command, e.g. { on_off: true, brightness: 50 }.
   * Published as DeviceCommandV1; the owning adapter encodes & applies it.
   */
  sendCommand: (deviceId: string, set: Record<string, unknown>): Promise<void> =>
    apiClient
      .post(`/api/device-control/${encodeURIComponent(deviceId)}/set`, set)
      .then(() => undefined),

  /** Bind a device to a zone (roadmap P0-3); pass null/'' to unassign. */
  assignZone: (deviceId: string, zoneId: string | null): Promise<void> =>
    apiClient
      .put(`/api/capability-devices/${encodeURIComponent(deviceId)}/zone`, { zoneId })
      .then(() => undefined),
};

/** Guid.Empty / blank zone ids both mean "unassigned". */
export const isUnassignedZone = (zoneId?: string | null): boolean =>
  !zoneId || zoneId.trim() === '' || zoneId === '00000000-0000-0000-0000-000000000000';
