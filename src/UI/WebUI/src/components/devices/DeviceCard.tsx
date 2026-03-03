// DeviceCard Component for WebUI
// Validates: Requirements 1.2, 10.2

import React from 'react';
import {
  Card,
  CardContent,
  Typography,
  Box,
  Chip,
} from '@mui/material';
import {
  Lightbulb,
  Sensors,
  ToggleOn,
  Thermostat,
  DeviceUnknown,
  Circle,
} from '@mui/icons-material';
import { Device, DeviceType } from '../../types/device';
import { formatDistanceToNow } from 'date-fns';

interface DeviceCardProps {
  device: Device;
  children?: React.ReactNode;
}

// Map device types to icons
const getDeviceIcon = (type: DeviceType): React.ReactElement => {
  const iconProps = { fontSize: 'large' as const };
  
  switch (type) {
    case 'Light':
      return <Lightbulb {...iconProps} />;
    case 'Sensor':
      return <Sensors {...iconProps} />;
    case 'Switch':
      return <ToggleOn {...iconProps} />;
    case 'Thermostat':
      return <Thermostat {...iconProps} />;
    case 'Unknown':
    default:
      return <DeviceUnknown {...iconProps} />;
  }
};

// Get status color
const getStatusColor = (status: string): 'success' | 'error' | 'warning' | 'default' => {
  switch (status) {
    case 'Online':
      return 'success';
    case 'Offline':
      return 'default';
    case 'Error':
      return 'error';
    default:
      return 'warning';
  }
};

/**
 * DeviceCard component displays device information and controls
 * Shows device name, type, status, online indicator, and last seen timestamp
 */
export const DeviceCard: React.FC<DeviceCardProps> = ({ device, children }) => {
  const lastSeenText = React.useMemo(() => {
    try {
      const date = device.lastSeen instanceof Date 
        ? device.lastSeen 
        : new Date(device.lastSeen);
      return formatDistanceToNow(date, { addSuffix: true });
    } catch {
      return 'Unknown';
    }
  }, [device.lastSeen]);

  return (
    <Card
      sx={{
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        transition: 'box-shadow 0.3s ease',
        '&:hover': {
          boxShadow: '0 4px 12px rgba(0, 0, 0, 0.15)',
        },
      }}
    >
      <CardContent sx={{ flexGrow: 1, display: 'flex', flexDirection: 'column' }}>
        {/* Header: Icon, Name, and Online Status */}
        <Box display="flex" alignItems="flex-start" justifyContent="space-between" mb={2}>
          <Box display="flex" alignItems="center" gap={1.5} flex={1}>
            <Box color="primary.main">
              {getDeviceIcon(device.type)}
            </Box>
            <Box flex={1}>
              <Typography variant="h6" component="h3" noWrap>
                {device.name}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {device.type}
              </Typography>
            </Box>
          </Box>
          
          {/* Online Indicator */}
          <Box display="flex" alignItems="center" gap={0.5}>
            <Circle
              sx={{
                fontSize: 12,
                color: device.isOnline ? 'success.main' : 'text.disabled',
              }}
            />
            <Typography variant="caption" color="text.secondary">
              {device.isOnline ? 'Online' : 'Offline'}
            </Typography>
          </Box>
        </Box>

        {/* Status Chip */}
        <Box mb={2}>
          <Chip
            label={device.status}
            color={getStatusColor(device.status)}
            size="small"
            sx={{ fontWeight: 500 }}
          />
        </Box>

        {/* Control Area - Rendered by parent via children prop */}
        {children && (
          <Box mb={2} flexGrow={1}>
            {children}
          </Box>
        )}

        {/* Last Seen */}
        <Box mt="auto" pt={1} borderTop={1} borderColor="divider">
          <Typography variant="caption" color="text.secondary">
            Last seen: {lastSeenText}
          </Typography>
        </Box>
      </CardContent>
    </Card>
  );
};

export default DeviceCard;
