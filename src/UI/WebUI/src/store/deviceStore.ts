// Device Store for WebUI
// Validates: Requirements 1.1, 2.2, 3.2

import { create } from 'zustand';
import { Device, DeviceCommand } from '../types/device';
import { getAllDevices, sendDeviceCommand } from '../api/devices';

interface DeviceStore {
  // State
  devices: Device[];
  loading: boolean;
  error: string | null;
  pollInterval: number;
  intervalId: number | null;
  isPolling: boolean;

  // Actions
  fetchDevices: () => Promise<void>;
  updateDevice: (deviceId: string, updates: Partial<Device>) => void;
  sendCommand: (command: DeviceCommand) => Promise<void>;
  startPolling: () => void;
  stopPolling: () => void;
  setError: (error: string | null) => void;
  setLoading: (loading: boolean) => void;
}

export const useDeviceStore = create<DeviceStore>((set, get) => ({
  // Initial state
  devices: [],
  loading: false,
  error: null,
  pollInterval: 1000, // 1 second default
  intervalId: null,
  isPolling: false,

  // Fetch devices from API
  fetchDevices: async () => {
    try {
      set({ loading: true, error: null });
      
      const devices = await getAllDevices();
      
      set({ devices, loading: false });
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : 'Failed to fetch devices';
      set({ error: errorMessage, loading: false });
    }
  },

  // Update a specific device in the store
  updateDevice: (deviceId: string, updates: Partial<Device>) => {
    set((state) => ({
      devices: state.devices.map((device) =>
        device.deviceId === deviceId
          ? { ...device, ...updates }
          : device
      ),
    }));
  },

  // Send command to device
  sendCommand: async (command: DeviceCommand) => {
    try {
      set({ loading: true, error: null });
      
      await sendDeviceCommand(command);
      
      set({ loading: false });
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : 'Failed to send command';
      set({ error: errorMessage, loading: false });
      throw error; // Re-throw to allow caller to handle
    }
  },

  // Start polling for device updates
  startPolling: () => {
    const { isPolling, pollInterval, fetchDevices, stopPolling } = get();
    
    // Don't start if already polling
    if (isPolling) {
      return;
    }

    // Initial fetch
    fetchDevices();

    // Setup polling with Page Visibility API support
    const poll = () => {
      // Only poll if page is visible
      if (!document.hidden) {
        fetchDevices();
      }
    };

    const id = setInterval(poll, pollInterval);
    
    set({ intervalId: id, isPolling: true });

    // Handle page visibility changes
    const handleVisibilityChange = () => {
      if (document.hidden) {
        // Page is hidden, polling continues but won't fetch
        console.log('Page hidden, pausing device polling');
      } else {
        // Page is visible again, fetch immediately
        console.log('Page visible, resuming device polling');
        fetchDevices();
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    
    // Store cleanup function
    (window as any).__deviceStoreCleanup = () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange);
      stopPolling();
    };
  },

  // Stop polling
  stopPolling: () => {
    const { intervalId } = get();
    
    if (intervalId) {
      clearInterval(intervalId);
      set({ intervalId: null, isPolling: false });
    }

    // Cleanup visibility listener
    if ((window as any).__deviceStoreCleanup) {
      delete (window as any).__deviceStoreCleanup;
    }
  },

  // Set error state
  setError: (error: string | null) => {
    set({ error });
  },

  // Set loading state
  setLoading: (loading: boolean) => {
    set({ loading });
  },
}));
