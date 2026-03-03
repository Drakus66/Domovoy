// Device API Service
// Implements device-related API calls
// Validates: Requirements 1.1, 2.2, 9.1

import apiClient from './client';
import { Device, DeviceCommand } from '../types/device';
import { ApiResponse } from '../types/api';

/**
 * Get all devices from the API
 * @returns Promise with array of devices
 */
export const getAllDevices = async (): Promise<Device[]> => {
  const response = await apiClient.get<Device[]>('/api/devices');
  return response.data;
};

/**
 * Get a specific device by ID
 * @param deviceId - The ID of the device to fetch
 * @returns Promise with device data
 */
export const getDeviceById = async (deviceId: string): Promise<Device> => {
  const response = await apiClient.get<Device>(`/api/devices/${deviceId}`);
  return response.data;
};

/**
 * Update device state
 * @param deviceId - The ID of the device to update
 * @param state - The new state data
 * @returns Promise with updated device
 */
export const updateDeviceState = async (
  deviceId: string,
  state: Record<string, any>
): Promise<Device> => {
  const response = await apiClient.patch<Device>(
    `/api/devices/${deviceId}/state`,
    state
  );
  return response.data;
};

/**
 * Send a command to a device
 * @param command - The device command to execute
 * @returns Promise with command result
 */
export const sendDeviceCommand = async (
  command: DeviceCommand
): Promise<ApiResponse<any>> => {
  const response = await apiClient.post<ApiResponse<any>>(
    '/api/device-control/command',
    command
  );
  return response.data;
};
