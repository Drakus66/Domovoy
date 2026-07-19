// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Button, Typography } from '@mui/material';
import DevicesIcon from '@mui/icons-material/Devices';
import { Link } from 'react-router-dom';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import type { CommandFn } from '../devices/CapabilityControls';
import { asBool, asNum } from '../devices/deviceVisuals';
import ZoneGroupedGrid from './ZoneGroupedGrid';

/**
 * The default home-screen tab: the house room by room. Only user-facing devices arrive here
 * (service devices — System sensors, block projections — live in the /devices registry), each
 * zone header carries a small climate/light digest so the room reads at a glance.
 */
export default function OverviewTab({
  devices, zoneName, loading, onOpen, onCommand,
}: {
  /** User-facing devices only — the caller filters service devices out. */
  devices: CapabilityDevice[];
  zoneName: (zoneId?: string | null) => string;
  loading: boolean;
  onOpen: (d: CapabilityDevice) => void;
  onCommand: CommandFn;
}) {
  const { t } = useTranslation('devices');

  const zoneSummary = useCallback((items: CapabilityDevice[]) => {
    const temps = items.map((d) => d.state?.temperature).filter((v) => v !== undefined).map(asNum);
    const lightsOn = items.filter(
      (d) => d.capabilities.some((c) => c.id === 'brightness') && asBool(d.state?.on_off),
    ).length;
    const parts: string[] = [];
    if (temps.length > 0) {
      parts.push(`${Math.round((temps.reduce((s, v) => s + v, 0) / temps.length) * 10) / 10} °C`);
    }
    if (lightsOn > 0) parts.push(t('overview.lightsOn', { count: lightsOn }));
    if (parts.length === 0) return null;
    return (
      <Typography variant="caption" color="text.secondary" noWrap>
        {parts.join(' · ')}
      </Typography>
    );
  }, [t]);

  if (devices.length === 0 && !loading) {
    return (
      <Box textAlign="center" py={8}>
        <DevicesIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
        <Typography color="text.secondary" mb={2}>{t('empty.noDevices')}</Typography>
        <Button component={Link} to="/devices" variant="outlined" size="small">
          {t('overview.openRegistry')}
        </Button>
      </Box>
    );
  }

  return (
    <ZoneGroupedGrid
      devices={devices}
      zoneName={zoneName}
      unassignedLabel={t('unassigned')}
      onOpen={onOpen}
      onCommand={onCommand}
      zoneSummary={zoneSummary}
    />
  );
}
