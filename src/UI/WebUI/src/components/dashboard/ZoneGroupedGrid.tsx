import { useMemo } from 'react';
import { Box, Chip, Divider, Grid, Stack, Typography } from '@mui/material';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import DeviceTile from '../devices/DeviceTile';
import type { CommandFn } from '../devices/CapabilityControls';

/**
 * The zone-grouped responsive device grid shared by the All tab and the sphere tabs:
 * one block per zone (named zones alphabetically, Unassigned last), devices sorted by name.
 */
export default function ZoneGroupedGrid({
  devices, zoneName, unassignedLabel, onOpen, onCommand,
}: {
  devices: CapabilityDevice[];
  zoneName: (zoneId?: string | null) => string;
  unassignedLabel: string;
  onOpen: (d: CapabilityDevice) => void;
  onCommand: CommandFn;
}) {
  const grouped = useMemo(() => {
    const map = new Map<string, CapabilityDevice[]>();
    for (const d of devices) {
      const key = zoneName(d.zoneId);
      const bucket = map.get(key) ?? [];
      bucket.push(d);
      map.set(key, bucket);
    }
    return Array.from(map.entries())
      .sort(([a], [b]) => (a === unassignedLabel ? 1 : b === unassignedLabel ? -1 : a.localeCompare(b)))
      .map(([zone, items]) => ({ zone, items: items.sort((x, y) => x.name.localeCompare(y.name)) }));
  }, [devices, zoneName, unassignedLabel]);

  return (
    <Stack spacing={4}>
      {grouped.map(({ zone, items }) => (
        <Box key={zone}>
          <Stack direction="row" alignItems="center" spacing={1.5} mb={1.5}>
            <Typography variant="h6" fontWeight={700}>{zone}</Typography>
            <Chip size="small" label={items.length} variant="outlined" />
            <Divider sx={{ flex: 1 }} />
          </Stack>
          <Grid container spacing={2}>
            {items.map((device) => (
              <Grid item xs={12} sm={6} md={4} lg={3} key={device.id}>
                <DeviceTile device={device} onOpen={onOpen} onCommand={onCommand} />
              </Grid>
            ))}
          </Grid>
        </Box>
      ))}
    </Stack>
  );
}
