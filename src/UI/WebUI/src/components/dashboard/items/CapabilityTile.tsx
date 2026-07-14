// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { Box, Card, CardActionArea, Stack, Switch, Typography } from '@mui/material';
import type { Capability, CapabilityDevice } from '../../../api/capabilityDevices';
import type { CommandFn } from '../../devices/CapabilityControls';
import {
  asBool, capabilityIcon, capabilityLabel, deviceCategory, formatCapabilityValue, CATEGORY_ACCENT,
} from '../../devices/deviceVisuals';

/**
 * A tile for ONE capability of a device — the "pick exactly what the remote shows" widget
 * (e.g. just the humidity of a multi-sensor). Styled like DeviceTile; a writable boolean gets
 * a quick toggle, everything else shows the formatted value. Tapping opens the device drawer.
 */
export default function CapabilityTile({
  device, cap, onOpen, onCommand,
}: {
  device: CapabilityDevice;
  cap: Capability;
  onOpen: (d: CapabilityDevice) => void;
  onCommand: CommandFn;
}) {
  const accent = CATEGORY_ACCENT[deviceCategory(device)];
  const Icon = capabilityIcon(cap.id);
  const offline = !device.isOnline;
  const value = device.state?.[cap.id];

  const quickToggle = cap.kind === 'Boolean' && cap.writable;
  const on = quickToggle && asBool(value);
  const glow = on && !offline;

  return (
    <Card
      sx={{
        height: '100%',
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
              }}
            >
              <Icon />
            </Box>
            {quickToggle && (
              <Box onClick={(e) => e.stopPropagation()} onMouseDown={(e) => e.stopPropagation()}>
                <Switch
                  checked={on}
                  color="success"
                  disabled={offline}
                  onChange={(e) => onCommand(device.id, { [cap.id]: e.target.checked })}
                />
              </Box>
            )}
          </Stack>

          <Box flex={1} minWidth={0}>
            <Typography variant="subtitle1" fontWeight={700} noWrap title={device.name}>
              {device.name}
            </Typography>
            <Typography variant="caption" color="text.secondary" noWrap display="block">
              {capabilityLabel(cap.id)}
            </Typography>
          </Box>

          <Typography variant="body2" fontWeight={600} noWrap sx={{ color: glow ? accent : 'text.primary' }}>
            {formatCapabilityValue(cap, value)}
          </Typography>
        </Stack>
      </CardActionArea>
    </Card>
  );
}
