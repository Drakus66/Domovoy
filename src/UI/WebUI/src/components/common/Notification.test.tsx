// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Notification Component Tests
// Validates: Requirements 8.2, 8.3, 8.5

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n from 'i18next';
import { NotificationContainer } from './Notification';
import { useUIStore } from '../../store/uiStore';

describe('NotificationContainer', () => {
  beforeEach(() => {
    // Clear notifications before each test
    useUIStore.getState().clearAllNotifications();
  });

  afterEach(() => {
    vi.clearAllTimers();
  });

  it('should render nothing when there are no notifications', () => {
    const { container } = render(<NotificationContainer />);
    expect(container.firstChild).toBeNull();
  });

  it('should display a success notification', async () => {
    render(<NotificationContainer />);
    
    act(() => {
      useUIStore.getState().showNotification('success', 'Operation successful');
    });
    
    await waitFor(() => {
      expect(screen.getByText('Operation successful')).toBeInTheDocument();
    });
  });

  it('should display an error notification', async () => {
    render(<NotificationContainer />);
    
    act(() => {
      useUIStore.getState().showNotification('error', 'Operation failed');
    });
    
    await waitFor(() => {
      expect(screen.getByText('Operation failed')).toBeInTheDocument();
    });
  });

  it('should display an info notification', async () => {
    render(<NotificationContainer />);
    
    act(() => {
      useUIStore.getState().showNotification('info', 'Information message');
    });
    
    await waitFor(() => {
      expect(screen.getByText('Information message')).toBeInTheDocument();
    });
  });

  it('should display multiple notifications', async () => {
    render(<NotificationContainer />);
    
    act(() => {
      useUIStore.getState().showNotification('success', 'First notification');
      useUIStore.getState().showNotification('error', 'Second notification');
      useUIStore.getState().showNotification('info', 'Third notification');
    });
    
    await waitFor(() => {
      expect(screen.getByText('First notification')).toBeInTheDocument();
      expect(screen.getByText('Second notification')).toBeInTheDocument();
      expect(screen.getByText('Third notification')).toBeInTheDocument();
    });
  });

  it('should dismiss notification when close button is clicked', async () => {
    const user = userEvent.setup();
    render(<NotificationContainer />);
    
    act(() => {
      useUIStore.getState().showNotification('success', 'Test notification');
    });
    
    await waitFor(() => {
      expect(screen.getByText('Test notification')).toBeInTheDocument();
    });
    
    const closeButton = screen.getByLabelText(i18n.t('common:actions.close'));
    await user.click(closeButton);
    
    await waitFor(() => {
      expect(screen.queryByText('Test notification')).not.toBeInTheDocument();
    });
  });

  it('should auto-dismiss notification after specified duration', async () => {
    vi.useFakeTimers();
    
    const { rerender } = render(<NotificationContainer />);
    
    act(() => {
      useUIStore.getState().showNotification('success', 'Auto-dismiss notification', 100);
    });
    
    // Force a rerender to pick up the state change
    rerender(<NotificationContainer />);
    
    expect(screen.getByText('Auto-dismiss notification')).toBeInTheDocument();
    
    // Fast-forward time by 100ms
    act(() => {
      vi.advanceTimersByTime(100);
    });
    
    // Force a rerender
    rerender(<NotificationContainer />);
    
    // Check that notification is removed
    expect(screen.queryByText('Auto-dismiss notification')).not.toBeInTheDocument();
    
    vi.useRealTimers();
  });

  it('should not auto-dismiss notification when duration is not specified', async () => {
    vi.useFakeTimers();
    
    const { rerender } = render(<NotificationContainer />);
    
    act(() => {
      useUIStore.getState().showNotification('success', 'Persistent notification', 0);
    });
    
    // Force a rerender
    rerender(<NotificationContainer />);
    
    expect(screen.getByText('Persistent notification')).toBeInTheDocument();
    
    // Fast-forward time by 10 seconds
    act(() => {
      vi.advanceTimersByTime(10000);
    });
    
    // Force a rerender
    rerender(<NotificationContainer />);
    
    // Notification should still be visible
    expect(screen.getByText('Persistent notification')).toBeInTheDocument();
    
    vi.useRealTimers();
  });

  it('should handle notification types correctly', async () => {
    const { rerender } = render(<NotificationContainer />);
    
    const types: Array<'success' | 'error' | 'info' | 'warning'> = ['success', 'error', 'info', 'warning'];
    
    act(() => {
      types.forEach((type) => {
        useUIStore.getState().showNotification(type, `${type} message`);
      });
    });
    
    // Force a rerender
    rerender(<NotificationContainer />);
    
    // Check all types are present
    types.forEach((type) => {
      expect(screen.getByText(`${type} message`)).toBeInTheDocument();
    });
  });

  it('should clear all notifications when clearAllNotifications is called', async () => {
    const { rerender } = render(<NotificationContainer />);
    
    act(() => {
      useUIStore.getState().showNotification('success', 'First');
      useUIStore.getState().showNotification('error', 'Second');
      useUIStore.getState().showNotification('info', 'Third');
    });
    
    // Force a rerender
    rerender(<NotificationContainer />);
    
    // Verify all are present
    expect(screen.getByText('First')).toBeInTheDocument();
    expect(screen.getByText('Second')).toBeInTheDocument();
    expect(screen.getByText('Third')).toBeInTheDocument();
    
    act(() => {
      useUIStore.getState().clearAllNotifications();
    });
    
    // Force a rerender
    rerender(<NotificationContainer />);
    
    // Check that they're gone
    expect(screen.queryByText('First')).not.toBeInTheDocument();
    expect(screen.queryByText('Second')).not.toBeInTheDocument();
    expect(screen.queryByText('Third')).not.toBeInTheDocument();
  });
});
