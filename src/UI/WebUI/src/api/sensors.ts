// Sensor API Service
// Implements sensor-related API calls
// Validates: Requirements 3.1, 4.3, 9.3

import apiClient from './client';
import { Sensor, SensorReading } from '../types/sensor';

/**
 * Get all sensors from the API
 * @returns Promise with array of sensors
 */
export const getAllSensors = async (): Promise<Sensor[]> => {
  const response = await apiClient.get<Sensor[]>('/api/sensors');
  return response.data;
};

/**
 * Get current readings for a specific sensor
 * @param sensorId - The ID of the sensor
 * @returns Promise with sensor readings
 */
export const getSensorReadings = async (sensorId: string): Promise<SensorReading[]> => {
  const response = await apiClient.get<SensorReading[]>(
    `/api/sensors/${sensorId}/readings`
  );
  return response.data;
};

/**
 * Get historical sensor data for a time range
 * @param sensorId - The ID of the sensor
 * @param start - Start timestamp (ISO string or Date)
 * @param end - End timestamp (ISO string or Date)
 * @returns Promise with historical sensor readings
 */
export const getSensorHistory = async (
  sensorId: string,
  start: string | Date,
  end: string | Date
): Promise<SensorReading[]> => {
  // Convert Date objects to ISO strings if needed
  const startTime = start instanceof Date ? start.toISOString() : start;
  const endTime = end instanceof Date ? end.toISOString() : end;
  
  const response = await apiClient.get<SensorReading[]>(
    `/api/sensors/${sensorId}/history`,
    {
      params: {
        start: startTime,
        end: endTime,
      },
    }
  );
  return response.data;
};
