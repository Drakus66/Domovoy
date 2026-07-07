import { Card, CardContent, Stack, Typography, Box } from '@mui/material';
import type { Capability, CapabilityDevice } from '../../../api/capabilityDevices';
import { capabilityIcon, capabilityLabel } from '../../devices/deviceVisuals';
import TelemetryChart from '../../charts/TelemetryChart';

/**
 * A telemetry-trend widget for one numeric capability of a device (reuses the Epic 1B chart).
 * Window/bucket come from the item's params (editor presets: 6h/minute, 24h/hour, week/day).
 */
export default function ChartTile({
  device, cap, hours, bucket,
}: {
  device: CapabilityDevice;
  cap: Capability;
  hours: number;
  bucket: 'minute' | 'hour' | 'day';
}) {
  const Icon = capabilityIcon(cap.id);
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" alignItems="center" spacing={1} mb={1} minWidth={0}>
          <Box sx={{ color: 'text.secondary', display: 'flex' }}><Icon fontSize="small" /></Box>
          <Typography variant="subtitle2" fontWeight={700} noWrap>
            {device.name} · {capabilityLabel(cap.id)}
          </Typography>
        </Stack>
        <TelemetryChart
          capabilityId={cap.id}
          deviceId={device.id}
          unit={cap.unit}
          hours={hours}
          bucket={bucket}
          height={180}
        />
      </CardContent>
    </Card>
  );
}
