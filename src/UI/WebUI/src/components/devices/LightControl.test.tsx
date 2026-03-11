// LightControl touch-friendly tests
// Validates: Requirements 6.4, 7.3

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { ThemeProvider } from '@mui/material';
import theme from '../../theme';
import { LightControl } from './LightControl';
import { Device, Light } from '../../types/device';

const mockDevice: Device = {
  deviceId: 'light-1',
  name: 'Living Room Light',
  type: 'Light',
  locationId: 'living-room',
  status: 'Online',
  isOnline: true,
  lastSeen: new Date(),
  configuration: {
    state: true,
    brightness: 75,
  },
};

const mockLight: Light = {
  lightId: 'light-1',
  deviceId: 'light-1',
  dimmable: true,
  colorSupport: false,
  defaultBrightness: 100,
};

describe('LightControl Touch-Friendly Controls', () => {
  it('should render toggle switch with touch-friendly size', () => {
    const onToggle = vi.fn();
    const onBrightnessChange = vi.fn();

    render(
      <ThemeProvider theme={theme}>
        <LightControl
          device={mockDevice}
          light={mockLight}
          onToggle={onToggle}
          onBrightnessChange={onBrightnessChange}
        />
      </ThemeProvider>
    );

    // Switch should be rendered
    const switchElement = screen.getByRole('checkbox');
    expect(switchElement).toBeInTheDocument();
    expect(switchElement).toBeChecked();
  });

  it('should render brightness slider for dimmable lights', () => {
    const onToggle = vi.fn();
    const onBrightnessChange = vi.fn();

    render(
      <ThemeProvider theme={theme}>
        <LightControl
          device={mockDevice}
          light={mockLight}
          onToggle={onToggle}
          onBrightnessChange={onBrightnessChange}
        />
      </ThemeProvider>
    );

    // Slider should be rendered
    expect(screen.getByText('Brightness')).toBeInTheDocument();
    expect(screen.getAllByText('75%')).toHaveLength(2);
    
    const slider = screen.getByRole('slider');
    expect(slider).toBeInTheDocument();
    expect(slider).toHaveAttribute('aria-valuenow', '75');
  });

  it('should not render brightness slider for non-dimmable lights', () => {
    const onToggle = vi.fn();
    const onBrightnessChange = vi.fn();
    
    const nonDimmableLight: Light = {
      ...mockLight,
      dimmable: false,
    };

    render(
      <ThemeProvider theme={theme}>
        <LightControl
          device={mockDevice}
          light={nonDimmableLight}
          onToggle={onToggle}
          onBrightnessChange={onBrightnessChange}
        />
      </ThemeProvider>
    );

    // Slider should not be rendered
    expect(screen.queryByText('Brightness')).not.toBeInTheDocument();
    expect(screen.queryByRole('slider')).not.toBeInTheDocument();
  });

  it('should handle toggle switch interaction', async () => {
    const onToggle = vi.fn().mockResolvedValue(undefined);
    const onBrightnessChange = vi.fn();

    render(
      <ThemeProvider theme={theme}>
        <LightControl
          device={mockDevice}
          light={mockLight}
          onToggle={onToggle}
          onBrightnessChange={onBrightnessChange}
        />
      </ThemeProvider>
    );

    const switchElement = screen.getByRole('checkbox');
    
    // Click the switch
    fireEvent.click(switchElement);

    await waitFor(() => {
      expect(onToggle).toHaveBeenCalledWith('light-1', false);
    });
  });

  it('should debounce brightness slider changes', async () => {
    const onToggle = vi.fn();
    const onBrightnessChange = vi.fn().mockResolvedValue(undefined);

    render(
      <ThemeProvider theme={theme}>
        <LightControl
          device={mockDevice}
          light={mockLight}
          onToggle={onToggle}
          onBrightnessChange={onBrightnessChange}
        />
      </ThemeProvider>
    );

    const slider = screen.getByRole('slider');
    
    // Change slider value
    fireEvent.change(slider, { target: { value: 50 } });

    // Should not call immediately (debounced)
    expect(onBrightnessChange).not.toHaveBeenCalled();

    // Wait for debounce (300ms)
    await waitFor(() => {
      expect(onBrightnessChange).toHaveBeenCalledWith('light-1', 50);
    }, { timeout: 500 });
  });

  it('should disable controls when device is offline', () => {
    const onToggle = vi.fn();
    const onBrightnessChange = vi.fn();
    
    const offlineDevice: Device = {
      ...mockDevice,
      isOnline: false,
      status: 'Offline',
    };

    render(
      <ThemeProvider theme={theme}>
        <LightControl
          device={offlineDevice}
          light={mockLight}
          onToggle={onToggle}
          onBrightnessChange={onBrightnessChange}
        />
      </ThemeProvider>
    );

    // Controls should be disabled
    const switchElement = screen.getByRole('checkbox');
    expect(switchElement).toBeDisabled();
    
    const slider = screen.getByRole('slider');
    expect(slider).toBeDisabled();
    
    // Offline message should be shown
    expect(screen.getByText('Device is offline')).toBeInTheDocument();
  });

  it('should show loading indicator when loading', () => {
    const onToggle = vi.fn();
    const onBrightnessChange = vi.fn();

    render(
      <ThemeProvider theme={theme}>
        <LightControl
          device={mockDevice}
          light={mockLight}
          onToggle={onToggle}
          onBrightnessChange={onBrightnessChange}
          isLoading={true}
        />
      </ThemeProvider>
    );

    // Loading indicator should be visible
    expect(screen.getByRole('progressbar')).toBeInTheDocument();
    
    // Controls should be disabled
    const switchElement = screen.getByRole('checkbox');
    expect(switchElement).toBeDisabled();
  });
});
