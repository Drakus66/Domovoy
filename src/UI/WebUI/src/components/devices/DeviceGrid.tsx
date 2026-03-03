// DeviceGrid Component for WebUI
// Validates: Requirements 1.3, 7.1, 7.2, 7.3

import React from 'react';
import {
  Grid,
  Box,
  Typography,
  Container,
} from '@mui/material';
import { DevicesOther } from '@mui/icons-material';
import { Device } from '../../types/device';
import { CardSkeleton } from '../common/Loading';

interface DeviceGridProps {
  devices: Device[];
  loading?: boolean;
  children?: (device: Device) => React.ReactNode;
}

/**
 * DeviceGrid component displays devices in a responsive grid layout
 * Desktop: multi-column, Tablet: 2-column, Mobile: 1-column
 */
export const DeviceGrid: React.FC<DeviceGridProps> = ({
  devices,
  loading = false,
  children,
}) => {
  // Show loading skeletons
  if (loading) {
    return (
      <Container maxWidth="xl">
        <CardSkeleton count={6} />
      </Container>
    );
  }

  // Show empty state
  if (devices.length === 0) {
    return (
      <Container maxWidth="xl">
        <Box
          display="flex"
          flexDirection="column"
          alignItems="center"
          justifyContent="center"
          minHeight="400px"
          textAlign="center"
          gap={2}
        >
          <DevicesOther 
            sx={{ 
              fontSize: 80, 
              color: 'text.disabled',
              opacity: 0.5,
            }} 
          />
          <Typography variant="h5" color="text.secondary" fontWeight={500}>
            No devices found
          </Typography>
          <Typography variant="body2" color="text.secondary" maxWidth={400}>
            There are no devices registered in your smart home system yet.
            Add devices to get started.
          </Typography>
        </Box>
      </Container>
    );
  }

  // Render device grid
  return (
    <Container maxWidth="xl">
      <Grid 
        container 
        spacing={3}
        sx={{
          // Ensure consistent spacing across breakpoints
          margin: 0,
          width: '100%',
        }}
      >
        {devices.map((device) => (
          <Grid 
            item 
            xs={12}      // Mobile: 1 column (full width)
            sm={6}       // Tablet: 2 columns
            md={4}       // Desktop: 3 columns
            lg={3}       // Large desktop: 4 columns
            key={device.deviceId}
          >
            {children ? children(device) : (
              <Box
                p={2}
                border={1}
                borderColor="divider"
                borderRadius={2}
              >
                <Typography variant="body1">{device.name}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {device.type}
                </Typography>
              </Box>
            )}
          </Grid>
        ))}
      </Grid>
    </Container>
  );
};

export default DeviceGrid;
