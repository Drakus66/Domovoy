// SensorDisplay Component for WebUI
// Validates: Requirements 3.1, 3.3, 3.4, 3.5

import React from 'react';
import {
  Box,
  Typography,
  Chip,
  Divider,
} from '@mui/material';
import { Warning, CheckCircle } from '@mui/icons-material';
import { Device } from '../../types/device';
import { Sensor, SensorReading, SensorType } from '../../types/sensor';
import { formatDistanceToNow } from 'date-fns';

interface SensorDisplayProps {
  device: Device;
  sensor: Sensor;
  readings?: SensorReading[];
}

// Map sensor types to units
const getSensorUnit = (type: SensorType): string => {
  switch (type) {
    case 'Temperature':
      return '°C';
    case 'Humidity':
      return '%';
    case 'Pressure':
      return 'hPa';
    case 'Light':
      return 'lux';
    case 'Motion':
      return '';
    default:
      return '';
  }
};

// Format sensor value with appropriate precision
const formatSensorValue = (value: number, precision: number): string => {
  return value.toFixed(precision);
};

// Check if sensor data is stale
const isDataStale = (timestamp: Date, updateFrequency: number): boolean => {
  const now = new Date();
  const timeDiff = now.getTime() - new Date(timestamp).getTime();
  // Consider stale if no update for 2x the update frequency (in milliseconds)
  const staleThreshold = updateFrequency * 2 * 1000;
  return timeDiff > staleThreshold;
};

/**
 * SensorDisplay component shows sensor readings with proper formatting
 * Displays current value, units, data freshness, and handles multiple readings
 */
export const SensorDisplay: React.FC<SensorDisplayProps> = ({
  device,
  sensor,
  readings = [],
}) => {
  // Get the most recent reading
  const latestReading = readings.length > 0 
    ? readings.reduce((latest, current) => 
        new Date(current.timestamp) > new Date(latest.timestamp) ? current : latest
      )
    : null;

  // Use sensor's lastValue if no readings provided
  const currentValue = latestReading?.value ?? sensor.lastValue;
  const currentTimestamp = latestReading?.timestamp ?? new Date();
  const unit = latestReading?.unit ?? getSensorUnit(sensor.type);

  // Check data freshness
  const isStale = latestReading 
    ? isDataStale(latestReading.timestamp, sensor.updateFrequency)
    : sensor.lastValue === null;

  // Format timestamp
  const timeAgo = React.useMemo(() => {
    try {
      const date = currentTimestamp instanceof Date 
        ? currentTimestamp 
        : new Date(currentTimestamp);
      return formatDistanceToNow(date, { addSuffix: true });
    } catch {
      return 'Unknown';
    }
  }, [currentTimestamp]);

  // Group readings by type if multiple sensor types exist
  const readingsByType = React.useMemo(() => {
    if (readings.length === 0) return null;
    
    const grouped = readings.reduce((acc, reading) => {
      const key = reading.unit || 'default';
      if (!acc[key]) {
        acc[key] = [];
      }
      acc[key].push(reading);
      return acc;
    }, {} as Record<string, SensorReading[]>);

    return grouped;
  }, [readings]);

  return (
    <Box>
      {/* Primary Reading */}
      <Box display="flex" alignItems="baseline" justifyContent="space-between" mb={2}>
        <Box>
          <Typography variant="h4" component="div" fontWeight={600}>
            {currentValue !== null ? (
              <>
                {formatSensorValue(currentValue, sensor.precision)}
                <Typography component="span" variant="h5" color="text.secondary" ml={0.5}>
                  {unit}
                </Typography>
              </>
            ) : (
              <Typography variant="body1" color="text.secondary">
                No data
              </Typography>
            )}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {sensor.type}
          </Typography>
        </Box>

        {/* Data Freshness Indicator */}
        <Box display="flex" alignItems="center" gap={0.5}>
          {isStale ? (
            <>
              <Warning fontSize="small" color="warning" />
              <Chip 
                label="Stale" 
                size="small" 
                color="warning" 
                variant="outlined"
              />
            </>
          ) : currentValue !== null ? (
            <>
              <CheckCircle fontSize="small" color="success" />
              <Chip 
                label="Fresh" 
                size="small" 
                color="success" 
                variant="outlined"
              />
            </>
          ) : null}
        </Box>
      </Box>

      {/* Last Updated */}
      <Typography variant="caption" color="text.secondary" display="block" mb={2}>
        Updated {timeAgo}
      </Typography>

      {/* Multiple Readings Display */}
      {readingsByType && Object.keys(readingsByType).length > 1 && (
        <>
          <Divider sx={{ my: 2 }} />
          <Typography variant="body2" fontWeight={500} mb={1}>
            All Readings
          </Typography>
          <Box display="flex" flexDirection="column" gap={1}>
            {Object.entries(readingsByType).map(([unitKey, unitReadings]) => {
              const latest = unitReadings[0];
              return (
                <Box 
                  key={unitKey}
                  display="flex" 
                  alignItems="center" 
                  justifyContent="space-between"
                  p={1}
                  bgcolor="background.default"
                  borderRadius={1}
                >
                  <Typography variant="body2" color="text.secondary">
                    {latest.unit || 'Value'}
                  </Typography>
                  <Typography variant="body2" fontWeight={500}>
                    {formatSensorValue(latest.value, sensor.precision)} {latest.unit}
                  </Typography>
                </Box>
              );
            })}
          </Box>
        </>
      )}

      {/* Offline indicator */}
      {!device.isOnline && (
        <Box mt={2}>
          <Chip 
            label="Device offline" 
            size="small" 
            color="error" 
            variant="outlined"
          />
        </Box>
      )}
    </Box>
  );
};

export default SensorDisplay;
