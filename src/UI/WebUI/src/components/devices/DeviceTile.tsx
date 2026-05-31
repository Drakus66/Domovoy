import { Box, Card, CardActionArea, Stack, Switch, Typography, LinearProgress, Tooltip } from '@mui/material';
import CircleIcon from '@mui/icons-material/Circle';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import type { CommandFn } from './CapabilityControls';
import { asBool, asNum, describeDevice, primaryCapability } from './deviceVisuals';

/**
 * Homey-style device tile: an icon badge that glows when the device is "on",
 * a headline state line, and a quick on/off toggle for switchable devices.
 * Tapping the body opens the full control drawer.
 */
export default function DeviceTile({
  device, onOpen, onCommand,
}: { device: CapabilityDevice; onOpen: (d: CapabilityDevice) => void; onCommand: CommandFn }) {
  const { accent, Icon, isActive, primary, secondary } = describeDevice(device);
  const offline = !device.isOnline;

  const prim = primaryCapability(device);
  const quickToggle = prim?.id === 'on_off' && prim.writable;
  const on = asBool(device.state?.on_off);
  const brightness = 'brightness' in (device.state ?? {}) ? asNum(device.state.brightness) : undefined;
  const glow = isActive && !offline;

  return (
    <Card
      sx={{
        height: '100%',
        position: 'relative',
        transition: 'border-color .2s, box-shadow .2s, transform .12s',
        borderColor: glow ? `${accent}66` : undefined,
        boxShadow: glow ? `0 0 0 1px ${accent}40, 0 10px 26px -12px ${accent}88` : undefined,
        opacity: offline ? 0.55 : 1,
        '&:hover': { transform: 'translateY(-2px)' },
      }}
    >
      <CardActionArea onClick={() => onOpen(device)} sx={{ height: '100%', p: 2, alignItems: 'stretch' }}>
        <Stack height="100%" spacing={1.25}>
          <Stack direction="row" justifyContent="space-between" alignItems="flex-start">
            <Box
              sx={{
                width: 44, height: 44, borderRadius: 2.5, display: 'flex',
                alignItems: 'center', justifyContent: 'center',
                color: glow ? accent : 'text.secondary',
                bgcolor: glow ? `${accent}24` : 'action.hover',
                transition: 'color .2s, background-color .2s',
              }}
            >
              <Icon />
            </Box>

            {quickToggle ? (
              // Quick control must not open the drawer.
              <Box onClick={(e) => e.stopPropagation()} onMouseDown={(e) => e.stopPropagation()}>
                <Switch
                  checked={on}
                  color="success"
                  disabled={offline}
                  onChange={(e) => onCommand(device.id, { on_off: e.target.checked })}
                />
              </Box>
            ) : (
              <Tooltip title={offline ? 'Offline' : 'Online'}>
                <CircleIcon sx={{ fontSize: 10, mt: 1, color: offline ? 'text.disabled' : 'success.main' }} />
              </Tooltip>
            )}
          </Stack>

          <Box flex={1} minWidth={0}>
            <Typography variant="subtitle1" fontWeight={700} noWrap title={device.name}>
              {device.name}
            </Typography>
            <Typography variant="caption" color="text.secondary" noWrap display="block">
              {secondary}
            </Typography>
          </Box>

          <Typography variant="body2" fontWeight={600} noWrap sx={{ color: glow ? accent : 'text.primary' }}>
            {primary}
          </Typography>

          {brightness !== undefined && on && !offline && (
            <LinearProgress
              variant="determinate"
              value={Math.max(0, Math.min(100, brightness))}
              sx={{
                height: 5, borderRadius: 3,
                bgcolor: `${accent}22`,
                '& .MuiLinearProgress-bar': { bgcolor: accent },
              }}
            />
          )}
        </Stack>
      </CardActionArea>
    </Card>
  );
}
