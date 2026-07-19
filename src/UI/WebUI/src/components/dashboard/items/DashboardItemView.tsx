// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useTranslation } from 'react-i18next';
import { Card, CardContent, Stack, Typography, Box } from '@mui/material';
import HelpOutlineRoundedIcon from '@mui/icons-material/HelpOutlineRounded';
import NightShelterRoundedIcon from '@mui/icons-material/NightShelterRounded';
import type { CapabilityDevice } from '../../../api/capabilityDevices';
import type { DashboardItem } from '../../../api/dashboards';
import type { CommandFn } from '../../devices/CapabilityControls';
import DeviceTile from '../../devices/DeviceTile';
import CapabilityTile from './CapabilityTile';
import ChartTile from './ChartTile';
import ModesTile from './ModesTile';
import SceneTile from './SceneTile';

const asChartHours = (v: unknown): number => {
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) && n > 0 ? n : 24;
};

const asChartBucket = (v: unknown): 'minute' | 'hour' | 'day' =>
  v === 'minute' || v === 'day' ? v : 'hour';

/**
 * Renders one custom-dashboard item by type. A reference that no longer resolves (device
 * removed, capability gone) renders a quiet placeholder — never crashes, never silently
 * disappears, so the user can tidy the tab up in the editor.
 */
export default function DashboardItemView({
  item, deviceById, onOpen, onCommand,
}: {
  item: DashboardItem;
  deviceById: Map<string, CapabilityDevice>;
  onOpen: (d: CapabilityDevice) => void;
  onCommand: CommandFn;
}) {
  const { t } = useTranslation('dashboards');

  if (item.type === 'modes') return <ModesTile />;
  if (item.type === 'scene') return <SceneTile sceneId={(item.params?.sceneId as string) ?? ''} />;

  const device = item.deviceId ? deviceById.get(item.deviceId) : undefined;
  if (!device) return <PlaceholderCard text={t('placeholder.deviceGone')} moved />;

  if (item.type === 'device') {
    return <DeviceTile device={device} onOpen={onOpen} onCommand={onCommand} />;
  }

  const cap = device.capabilities.find((c) => c.id === item.capabilityId);

  if (item.type === 'capability') {
    // A capability that vanished still renders: the tile shows "—" for a missing value.
    const fallback = cap ?? { id: item.capabilityId ?? '', kind: 'Text', writable: false };
    return <CapabilityTile device={device} cap={fallback} onOpen={onOpen} onCommand={onCommand} />;
  }

  if (item.type === 'chart') {
    return (
      <ChartTile
        device={device}
        cap={cap ?? { id: item.capabilityId ?? '', kind: 'Number', writable: false }}
        hours={asChartHours(item.params?.hours)}
        bucket={asChartBucket(item.params?.bucket)}
      />
    );
  }

  return <PlaceholderCard text={t('placeholder.unknownType')} />;
}

/** "Chart items want the wide slot" — grid width hint per item type. */
export const itemGridWidth = (item: DashboardItem) =>
  item.type === 'chart' || item.type === 'modes'
    ? { xs: 12, md: 6 } as const
    : { xs: 12, sm: 6, md: 4, lg: 3 } as const;

function PlaceholderCard({ text, moved = false }: { text: string; moved?: boolean }) {
  const Icon = moved ? NightShelterRoundedIcon : HelpOutlineRoundedIcon;
  return (
    <Card variant="outlined" sx={{ height: '100%', opacity: 0.65, borderStyle: 'dashed' }}>
      <CardContent sx={{ height: '100%' }}>
        <Stack spacing={1.25} height="100%">
          <Box
            sx={{
              width: 44, height: 44, borderRadius: 2.5, display: 'flex',
              alignItems: 'center', justifyContent: 'center',
              color: 'text.disabled', bgcolor: 'action.hover',
            }}
          >
            <Icon />
          </Box>
          <Typography variant="body2" color="text.secondary">{text}</Typography>
        </Stack>
      </CardContent>
    </Card>
  );
}
