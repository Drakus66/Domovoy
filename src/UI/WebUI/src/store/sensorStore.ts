// Sensor Store for WebUI
// Validates: Requirements 3.1, 4.3

import { create } from 'zustand';
import { Sensor, SensorReading } from '../types/sensor';

interface TimeRange {
  start: Date;
  end: Date;
}

interface SensorStore {
  // State
  sensors: Sensor[];
  sensorReadings: Record<string, SensorReading[]>; // sensorId -> readings
  loading: boolean;
  error: string | null;
  pollInterval: number;
  intervalId: number | null;
  isPolling: boolean;

  // Actions
  fetchSensors: () => Promise<void>;
  fetchSensorHistory: (sensorId: string, timeRange: TimeRange) => Promise<void>;
  startPolling: () => void;
  stopPolling: () => void;
  setError: (error: string | null) => void;
  setLoading: (loading: boolean) => void;
}

export const useSensorStore = create<SensorStore>((set, get) => ({
  // Initial state
  sensors: [],
  sensorReadings: {},
  loading: false,
  error: null,
  pollInterval: 1000, // 1 second default
  intervalId: null,
  isPolling: false,

  // Fetch all sensors from API
  fetchSensors: async () => {
    try {
      set({ loading: true, error: null });
      
      // TODO: Replace with actual API call when api/sensors.ts is implemented
      // const response = await apiClient.get('/api/sensors');
      // const sensors = response.data;
      
      // Mock implementation for now
      const sensors: Sensor[] = [];
      
      set({ sensors, loading: false });
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : 'Failed to fetch sensors';
      set({ error: errorMessage, loading: false });
    }
  },

  // Fetch historical sensor data for a specific sensor
  fetchSensorHistory: async (sensorId: string, timeRange: TimeRange) => {
    try {
      set({ loading: true, error: null });
      
      // TODO: Replace with actual API call when api/sensors.ts is implemented
      // const response = await apiClient.get(`/api/sensors/${sensorId}/history`, {
      //   params: {
      //     start: timeRange.start.toISOString(),
      //     end: timeRange.end.toISOString()
      //   }
      // });
      // const readings = response.data;
      
      // Mock implementation for now
      const readings: SensorReading[] = [];
      
      // Suppress unused variable warning - timeRange will be used when API is implemented
      void timeRange;
      
      set((state) => ({
        sensorReadings: {
          ...state.sensorReadings,
          [sensorId]: readings,
        },
        loading: false,
      }));
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : 'Failed to fetch sensor history';
      set({ error: errorMessage, loading: false });
    }
  },

  // Start polling for sensor updates
  startPolling: () => {
    const { isPolling, pollInterval, fetchSensors, stopPolling } = get();
    
    // Don't start if already polling
    if (isPolling) {
      return;
    }

    // Initial fetch
    fetchSensors();

    // Setup polling with Page Visibility API support
    const poll = () => {
      // Only poll if page is visible
      if (!document.hidden) {
        fetchSensors();
      }
    };

    const id = setInterval(poll, pollInterval);
    
    set({ intervalId: id, isPolling: true });

    // Handle page visibility changes
    const handleVisibilityChange = () => {
      if (document.hidden) {
        // Page is hidden, polling continues but won't fetch
        console.log('Page hidden, pausing sensor polling');
      } else {
        // Page is visible again, fetch immediately
        console.log('Page visible, resuming sensor polling');
        fetchSensors();
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    
    // Store cleanup function
    (window as any).__sensorStoreCleanup = () => {
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
    if ((window as any).__sensorStoreCleanup) {
      delete (window as any).__sensorStoreCleanup;
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
