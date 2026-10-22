// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useMemo, type ReactNode } from 'react';
import { Box, Chip, Divider, Grid, Stack, Typography } from '@mui/material';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import DeviceTile from '../devices/DeviceTile';
import { deviceLabel } from '../devices/deviceNaming';
import type { CommandFn } from '../devices/CapabilityControls';

/**
 * The zone-grouped responsive device grid shared by the Overview tab and the sphere tabs:
 * one block per zone (named zones alphabetically, Unassigned last), devices sorted by name.
 */
export default function ZoneGroupedGrid({
  devices, zoneName, unassignedLabel, onOpen, onCommand, zoneSummary,
}: {
  devices: CapabilityDevice[];
  zoneName: (zoneId?: string | null) => string;
  unassignedLabel: string;
  onOpen: (d: CapabilityDevice) => void;
  onCommand: CommandFn;
  /** Optional per-zone digest rendered in the zone header (the Overview tab's room summary). */
  zoneSummary?: (devices: CapabilityDevice[]) => ReactNode;
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
      .map(([zone, items]) => ({ zone, items: items.sort((x, y) => deviceLabel(x).localeCompare(deviceLabel(y))) }));
  }, [devices, zoneName, unassignedLabel]);

  return (
    <Stack spacing={4}>
      {grouped.map(({ zone, items }) => (
        <Box key={zone}>
          <Stack direction="row" alignItems="center" spacing={1.5} mb={1.5}>
            <Typography variant="h6" fontWeight={700}>{zone}</Typography>
            <Chip size="small" label={items.length} variant="outlined" />
            {zoneSummary?.(items)}
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
