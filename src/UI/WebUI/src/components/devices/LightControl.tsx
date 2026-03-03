// LightControl Component for WebUI
// Validates: Requirements 2.1, 2.3, 2.4, 8.1, 8.4

import React, { useState, useCallback, useEffect } from 'react';
import {
  Box,
  Switch,
  Slider,
  Typography,
  CircularProgress,
  FormControlLabel,
} from '@mui/material';
import { Device, Light } from '../../types/device';

interface LightControlProps {
  device: Device;
  light: Light;
  onToggle: (deviceId: string, state: boolean) => Promise<void>;
  onBrightnessChange: (deviceId: string, brightness: number) => Promise<void>;
  isLoading?: boolean;
}

/**
 * LightControl component provides controls for light devices
 * Includes toggle switch for on/off and brightness slider for dimmable lights
 */
export const LightControl: React.FC<LightControlProps> = ({
  device,
  light,
  onToggle,
  onBrightnessChange,
  isLoading = false,
}) => {
  // Local state for light on/off
  const [isOn, setIsOn] = useState<boolean>(
    device.configuration?.state === true || device.configuration?.state === 'on'
  );
  
  // Local state for brightness
  const [brightness, setBrightness] = useState<number>(
    device.configuration?.brightness ?? light.defaultBrightness ?? 100
  );
  
  // Debounce timer ref
  const debounceTimerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  // Update local state when device configuration changes
  useEffect(() => {
    if (device.configuration?.state !== undefined) {
      setIsOn(device.configuration.state === true || device.configuration.state === 'on');
    }
    if (device.configuration?.brightness !== undefined) {
      setBrightness(device.configuration.brightness);
    }
  }, [device.configuration]);

  // Handle toggle switch change
  const handleToggle = useCallback(async (event: React.ChangeEvent<HTMLInputElement>) => {
    const newState = event.target.checked;
    setIsOn(newState);
    
    try {
      await onToggle(device.deviceId, newState);
    } catch (error) {
      // Revert on error
      setIsOn(!newState);
    }
  }, [device.deviceId, onToggle]);

  // Handle brightness slider change (debounced)
  const handleBrightnessChange = useCallback(
    (_event: Event, value: number | number[]) => {
      const newBrightness = Array.isArray(value) ? value[0] : value;
      setBrightness(newBrightness);

      // Clear existing timer
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }

      // Set new debounced timer (300ms)
      debounceTimerRef.current = setTimeout(async () => {
        try {
          await onBrightnessChange(device.deviceId, newBrightness);
        } catch (error) {
          // Could revert brightness on error, but keeping user's input for now
          console.error('Failed to update brightness:', error);
        }
      }, 300);
    },
    [device.deviceId, onBrightnessChange]
  );

  // Cleanup debounce timer on unmount
  useEffect(() => {
    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }
    };
  }, []);

  return (
    <Box>
      {/* Toggle Switch */}
      <Box display="flex" alignItems="center" justifyContent="space-between" mb={2}>
        <FormControlLabel
          control={
            <Switch
              checked={isOn}
              onChange={handleToggle}
              disabled={isLoading || !device.isOnline}
              color="primary"
            />
          }
          label={
            <Typography variant="body2" fontWeight={500}>
              {isOn ? 'On' : 'Off'}
            </Typography>
          }
        />
        
        {/* Loading indicator */}
        {isLoading && (
          <CircularProgress size={20} />
        )}
      </Box>

      {/* Brightness Slider (only for dimmable lights) */}
      {light.dimmable && (
        <Box>
          <Box display="flex" alignItems="center" justifyContent="space-between" mb={1}>
            <Typography variant="body2" color="text.secondary">
              Brightness
            </Typography>
            <Typography variant="body2" fontWeight={500}>
              {brightness}%
            </Typography>
          </Box>
          
          <Slider
            value={brightness}
            onChange={handleBrightnessChange}
            disabled={isLoading || !device.isOnline || !isOn}
            min={0}
            max={100}
            step={1}
            valueLabelDisplay="auto"
            valueLabelFormat={(value) => `${value}%`}
            sx={{
              '& .MuiSlider-thumb': {
                transition: 'box-shadow 0.2s ease',
              },
              '& .MuiSlider-track': {
                transition: 'background-color 0.2s ease',
              },
            }}
          />
        </Box>
      )}

      {/* Offline indicator */}
      {!device.isOnline && (
        <Typography variant="caption" color="error" sx={{ mt: 1, display: 'block' }}>
          Device is offline
        </Typography>
      )}
    </Box>
  );
};

export default LightControl;
