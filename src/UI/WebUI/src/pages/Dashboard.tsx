// Dashboard Page for WebUI
// Validates: Requirements 1.1, 1.2, 3.2

import { useEffect, useState, useMemo } from 'react';
import {
  Container,
  Typography,
  Box,
  FormControl,
  InputLabel,
  Select,
  MenuItem,
  SelectChangeEvent,
  Alert,
  Button,
} from '@mui/material';
import { Refresh } from '@mui/icons-material';
import { useDeviceStore } from '../store/deviceStore';
import { useUIStore } from '../store/uiStore';
import { DeviceGrid } from '../components/devices/DeviceGrid';
import { DeviceCard } from '../components/devices/DeviceCard';
import { LightControl } from '../components/devices/LightControl';
import { SensorDisplay } from '../components/devices/SensorDisplay';
import { Device, DeviceType, Light } from '../types/device';
import { Sensor } from '../types/sensor';

function Dashboard() {
  const {
    devices,
    loading,
    error,
    fetchDevices,
    startPolling,
    stopPolling,
    isPolling,
  } = useDeviceStore();

  const { showNotification } = useUIStore();

  // Filter states
  const [deviceTypeFilter, setDeviceTypeFilter] = useState<DeviceType | 'All'>('All');
  const [locationFilter, setLocationFilter] = useState<string>('All');

  // Start polling on mount, stop on unmount
  useEffect(() => {
    startPolling();
    
    return () => {
      stopPolling();
    };
  }, [startPolling, stopPolling]);

  // Get unique device types and locations for filters
  const { deviceTypes, locations } = useMemo(() => {
    const types = new Set<DeviceType>();
    const locs = new Set<string>();
    
    devices.forEach((device) => {
      types.add(device.type);
      if (device.locationId) {
        locs.add(device.locationId);
      }
    });
    
    return {
      deviceTypes: Array.from(types),
      locations: Array.from(locs),
    };
  }, [devices]);

  // Filter devices based on selected filters
  const filteredDevices = useMemo(() => {
    return devices.filter((device) => {
      const typeMatch = deviceTypeFilter === 'All' || device.type === deviceTypeFilter;
      const locationMatch = locationFilter === 'All' || device.locationId === locationFilter;
      return typeMatch && locationMatch;
    });
  }, [devices, deviceTypeFilter, locationFilter]);

  // Handle device type filter change
  const handleDeviceTypeChange = (event: SelectChangeEvent) => {
    setDeviceTypeFilter(event.target.value as DeviceType | 'All');
  };

  // Handle location filter change
  const handleLocationChange = (event: SelectChangeEvent) => {
    setLocationFilter(event.target.value);
  };

  // Handle manual refresh
  const handleRefresh = () => {
    fetchDevices();
  };

  // Render device card with appropriate controls
  const renderDeviceCard = (device: Device) => {
    return (
      <DeviceCard key={device.deviceId} device={device}>
        {device.type === 'Light' && renderLightControl(device)}
        {device.type === 'Sensor' && renderSensorDisplay(device)}
      </DeviceCard>
    );
  };

  // Render light control for light devices
  const renderLightControl = (device: Device) => {
    // Extract light configuration from device
    const light: Light = {
      lightId: device.configuration?.lightId || device.deviceId,
      deviceId: device.deviceId,
      dimmable: device.configuration?.dimmable ?? false,
      colorSupport: device.configuration?.colorSupport ?? false,
      defaultBrightness: device.configuration?.defaultBrightness ?? 100,
    };

    return (
      <LightControl
        device={device}
        light={light}
        onToggle={handleLightToggle}
        onBrightnessChange={handleBrightnessChange}
      />
    );
  };

  // Render sensor display for sensor devices
  const renderSensorDisplay = (device: Device) => {
    // Extract sensor configuration from device
    const sensor: Sensor = {
      sensorId: device.configuration?.sensorId || device.deviceId,
      deviceId: device.deviceId,
      type: device.configuration?.sensorType || 'Temperature',
      updateFrequency: device.configuration?.updateFrequency ?? 60,
      precision: device.configuration?.precision ?? 1,
      lastValue: device.configuration?.lastValue ?? null,
    };

    return (
      <SensorDisplay
        device={device}
        sensor={sensor}
        readings={device.configuration?.readings || []}
      />
    );
  };

  // Handle light toggle command (will be implemented in sub-task 10.2)
  const handleLightToggle = async (deviceId: string, state: boolean) => {
    const device = devices.find((d) => d.deviceId === deviceId);
    if (!device) return;

    // Store previous state for rollback
    const previousState = device.configuration?.state;

    // Optimistic update
    useDeviceStore.getState().updateDevice(deviceId, {
      configuration: {
        ...device.configuration,
        state,
      },
    });

    try {
      // Send command to API
      await useDeviceStore.getState().sendCommand({
        deviceId,
        command: 'toggle',
        parameters: { state },
      });

      // Show success notification
      showNotification('success', `Light ${state ? 'turned on' : 'turned off'} successfully`);
    } catch (error) {
      // Revert state on failure
      useDeviceStore.getState().updateDevice(deviceId, {
        configuration: {
          ...device.configuration,
          state: previousState,
        },
      });

      // Show error notification
      const errorMessage = error instanceof Error ? error.message : 'Failed to toggle light';
      showNotification('error', `Failed to toggle light: ${errorMessage}`);
    }
  };

  // Handle brightness change command (will be implemented in sub-task 10.2)
  const handleBrightnessChange = async (deviceId: string, brightness: number) => {
    const device = devices.find((d) => d.deviceId === deviceId);
    if (!device) return;

    // Store previous brightness for rollback
    const previousBrightness = device.configuration?.brightness;

    // Optimistic update
    useDeviceStore.getState().updateDevice(deviceId, {
      configuration: {
        ...device.configuration,
        brightness,
      },
    });

    try {
      // Send command to API
      await useDeviceStore.getState().sendCommand({
        deviceId,
        command: 'setBrightness',
        parameters: { brightness },
      });

      // Show success notification
      showNotification('success', `Brightness set to ${brightness}%`);
    } catch (error) {
      // Revert brightness on failure
      useDeviceStore.getState().updateDevice(deviceId, {
        configuration: {
          ...device.configuration,
          brightness: previousBrightness,
        },
      });

      // Show error notification
      const errorMessage = error instanceof Error ? error.message : 'Failed to set brightness';
      showNotification('error', `Failed to set brightness: ${errorMessage}`);
    }
  };

  return (
    <Container maxWidth="xl">
      <Box sx={{ py: 4 }}>
        {/* Header */}
        <Box display="flex" alignItems="center" justifyContent="space-between" mb={3}>
          <Typography variant="h4" component="h1" fontWeight={600}>
            Dashboard
          </Typography>
          <Button
            variant="outlined"
            startIcon={<Refresh />}
            onClick={handleRefresh}
            disabled={loading}
          >
            Refresh
          </Button>
        </Box>

        {/* Filters */}
        <Box display="flex" gap={2} mb={3} flexWrap="wrap">
          <FormControl sx={{ minWidth: 200 }} size="small">
            <InputLabel id="device-type-filter-label">Device Type</InputLabel>
            <Select
              labelId="device-type-filter-label"
              id="device-type-filter"
              value={deviceTypeFilter}
              label="Device Type"
              onChange={handleDeviceTypeChange}
            >
              <MenuItem value="All">All Types</MenuItem>
              {deviceTypes.map((type) => (
                <MenuItem key={type} value={type}>
                  {type}
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          <FormControl sx={{ minWidth: 200 }} size="small">
            <InputLabel id="location-filter-label">Location</InputLabel>
            <Select
              labelId="location-filter-label"
              id="location-filter"
              value={locationFilter}
              label="Location"
              onChange={handleLocationChange}
            >
              <MenuItem value="All">All Locations</MenuItem>
              {locations.map((location) => (
                <MenuItem key={location} value={location}>
                  {location}
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          {/* Polling status indicator */}
          <Box display="flex" alignItems="center" ml="auto">
            <Typography variant="body2" color="text.secondary">
              {isPolling ? '🟢 Live updates active' : '🔴 Updates paused'}
            </Typography>
          </Box>
        </Box>

        {/* Error Alert */}
        {error && (
          <Alert severity="error" sx={{ mb: 3 }} onClose={() => useDeviceStore.getState().setError(null)}>
            {error}
          </Alert>
        )}

        {/* Device Grid */}
        <DeviceGrid devices={filteredDevices} loading={loading}>
          {renderDeviceCard}
        </DeviceGrid>
      </Box>
    </Container>
  );
}

export default Dashboard;
