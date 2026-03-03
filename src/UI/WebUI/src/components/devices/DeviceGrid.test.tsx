// DeviceGrid responsive layout tests
// Validates: Requirements 7.1, 7.2, 7.3

import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ThemeProvider } from '@mui/material/styles';
import theme from '../../theme';
import { DeviceGrid } from './DeviceGrid';
import { Device } from '../../types/device';

const mockDevices: Device[] = [
  {
    deviceId: '1',
    name: 'Living Room Light',
    type: 'Light',
    locationId: 'living-room',
    status: 'Online',
    isOnline: true,
    lastSeen: new Date(),
    configuration: {},
  },
  {
    deviceId: '2',
    name: 'Bedroom Sensor',
    type: 'Sensor',
    locationId: 'bedroom',
    status: 'Online',
    isOnline: true,
    lastSeen: new Date(),
    configuration: {},
  },
];

describe('DeviceGrid Responsive Layout', () => {
  it('should render devices in a grid', () => {
    render(
      <ThemeProvider theme={theme}>
        <DeviceGrid devices={mockDevices}>
          {(device) => <div data-testid={`device-${device.deviceId}`}>{device.name}</div>}
        </DeviceGrid>
      </ThemeProvider>
    );

    expect(screen.getByTestId('device-1')).toBeInTheDocument();
    expect(screen.getByTestId('device-2')).toBeInTheDocument();
  });

  it('should show loading skeletons when loading', () => {
    render(
      <ThemeProvider theme={theme}>
        <DeviceGrid devices={[]} loading={true} />
      </ThemeProvider>
    );

    // Loading component should be rendered
    const skeletons = screen.getAllByTestId('card-skeleton');
    expect(skeletons.length).toBeGreaterThan(0);
  });

  it('should show empty state when no devices', () => {
    render(
      <ThemeProvider theme={theme}>
        <DeviceGrid devices={[]} />
      </ThemeProvider>
    );

    expect(screen.getByText('No devices found')).toBeInTheDocument();
    expect(screen.getByText(/There are no devices registered/)).toBeInTheDocument();
  });

  it('should apply responsive grid breakpoints', () => {
    const { container } = render(
      <ThemeProvider theme={theme}>
        <DeviceGrid devices={mockDevices}>
          {(device) => <div>{device.name}</div>}
        </DeviceGrid>
      </ThemeProvider>
    );

    // Find Grid items
    const gridItems = container.querySelectorAll('.MuiGrid-item');
    expect(gridItems.length).toBe(2);

    // Check that Grid items have responsive classes
    gridItems.forEach((item) => {
      // xs={12} - Mobile: full width
      expect(item.classList.contains('MuiGrid-grid-xs-12')).toBe(true);
      
      // sm={6} - Tablet: 2 columns
      expect(item.classList.contains('MuiGrid-grid-sm-6')).toBe(true);
      
      // md={4} - Desktop: 3 columns
      expect(item.classList.contains('MuiGrid-grid-md-4')).toBe(true);
      
      // lg={3} - Large desktop: 4 columns
      expect(item.classList.contains('MuiGrid-grid-lg-3')).toBe(true);
    });
  });

  it('should render custom children for each device', () => {
    render(
      <ThemeProvider theme={theme}>
        <DeviceGrid devices={mockDevices}>
          {(device) => (
            <div data-testid={`custom-${device.deviceId}`}>
              Custom: {device.name}
            </div>
          )}
        </DeviceGrid>
      </ThemeProvider>
    );

    expect(screen.getByTestId('custom-1')).toHaveTextContent('Custom: Living Room Light');
    expect(screen.getByTestId('custom-2')).toHaveTextContent('Custom: Bedroom Sensor');
  });
});
