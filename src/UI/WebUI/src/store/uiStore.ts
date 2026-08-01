// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// UI Store for WebUI
// Validates: Requirements 8.2, 8.3

import { create } from 'zustand';

export type NotificationType = 'success' | 'error' | 'info' | 'warning';

/** An actionable button on a banner (roadmap Epic 3F). Server-side kinds post to /api/notifications/action; the
 *  client-only `open`/`dismiss` are handled in the banner. Labels are server-provided (already localized). */
export interface NotificationActionData {
  id: string;
  label: string;
  kind: string;
  params?: Record<string, string>;
}

export interface Notification {
  id: string;
  type: NotificationType;
  message: string;
  duration?: number; // milliseconds, undefined means no auto-dismiss
  timestamp: Date;
  actions?: NotificationActionData[];
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
    duration?: number,
    actions?: NotificationActionData[]
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
    duration: number = 5000, // Default 5 seconds
    actions?: NotificationActionData[]
  ): string => {
    const id = generateId();
    const notification: Notification = {
      id,
      type,
      message,
      duration,
      timestamp: new Date(),
      actions,
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
