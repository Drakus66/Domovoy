// Sensor Types for WebUI
// Validates: Requirements 3.1, 4.1

export type SensorType = 'Temperature' | 'Humidity' | 'Motion' | 'Light' | 'Pressure';

export interface Sensor {
  sensorId: string;
  deviceId: string;
  type: SensorType;
  updateFrequency: number;
  precision: number;
  lastValue: number | null;
}

export interface SensorReading {
  sensorId: string;
  timestamp: Date;
  value: number;
  unit: string;
}
