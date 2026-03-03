// Device Types for WebUI
// Validates: Requirements 1.1, 2.1

export type DeviceType = 'Light' | 'Sensor' | 'Switch' | 'Thermostat' | 'Unknown';

export type DeviceStatus = 'Online' | 'Offline' | 'Error';

export interface Device {
  deviceId: string;
  name: string;
  type: DeviceType;
  locationId: string;
  status: DeviceStatus;
  isOnline: boolean;
  lastSeen: Date;
  configuration: Record<string, any>;
}

export interface Light {
  lightId: string;
  deviceId: string;
  dimmable: boolean;
  colorSupport: boolean;
  defaultBrightness: number;
}

export interface DeviceCommand {
  deviceId: string;
  command: string;
  parameters: Record<string, any>;
}

export interface LightCommand extends DeviceCommand {
  command: 'toggle' | 'setBrightness' | 'setColor';
  parameters: {
    state?: boolean;
    brightness?: number;
    color?: string;
  };
}
