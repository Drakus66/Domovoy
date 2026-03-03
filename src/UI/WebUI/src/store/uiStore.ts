// UI Store for WebUI
// Validates: Requirements 8.2, 8.3

import { create } from 'zustand';

export type NotificationType = 'success' | 'error' | 'info' | 'warning';

export interface Notification {
  id: string;
  type: NotificationType;
  message: string;
  duration?: number; // milliseconds, undefined means no auto-dismiss
  timestamp: Date;
}

interface LoadingState {
  [key: string]: boolean;
}

interface UIStore {
  // State
  notifications: Notification[];
  loadingStates: LoadingState;
  globalLoading: boolean;

  // Actions
  showNotification: (
    type: NotificationType,
    message: string,
    duration?: number
  ) => string;
  dismissNotification: (id: string) => void;
  clearAllNotifications: () => void;
  setLoading: (key: string, loading: boolean) => void;
  setGlobalLoading: (loading: boolean) => void;
  isLoading: (key: string) => boolean;
}

// Generate unique ID for notifications
const generateId = (): string => {
  return `${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
};

export const useUIStore = create<UIStore>((set, get) => ({
  // Initial state
  notifications: [],
  loadingStates: {},
  globalLoading: false,

  // Show a notification
  showNotification: (
    type: NotificationType,
    message: string,
    duration: number = 5000 // Default 5 seconds
  ): string => {
    const id = generateId();
    const notification: Notification = {
      id,
      type,
      message,
      duration,
      timestamp: new Date(),
    };

    set((state) => ({
      notifications: [...state.notifications, notification],
    }));

    // Auto-dismiss if duration is specified
    if (duration && duration > 0) {
      setTimeout(() => {
        get().dismissNotification(id);
      }, duration);
    }

    return id;
  },

  // Dismiss a specific notification
  dismissNotification: (id: string) => {
    set((state) => ({
      notifications: state.notifications.filter((n) => n.id !== id),
    }));
  },

  // Clear all notifications
  clearAllNotifications: () => {
    set({ notifications: [] });
  },

  // Set loading state for a specific key
  setLoading: (key: string, loading: boolean) => {
    set((state) => ({
      loadingStates: {
        ...state.loadingStates,
        [key]: loading,
      },
    }));
  },

  // Set global loading state
  setGlobalLoading: (loading: boolean) => {
    set({ globalLoading: loading });
  },

  // Check if a specific key is loading
  isLoading: (key: string): boolean => {
    return get().loadingStates[key] || false;
  },
}));
