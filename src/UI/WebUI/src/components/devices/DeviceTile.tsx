// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { Box, Button, Card, CardActionArea, CardContent, Chip, Stack, Switch, Typography, LinearProgress, Tooltip, useTheme } from '@mui/material';
import { useTranslation } from 'react-i18next';
import CircleIcon from '@mui/icons-material/Circle';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import type { CommandFn } from './CapabilityControls';
import {
  asBool, asNum, capabilityLabel, describeDevice, formatCapabilityValue, primaryCapability, trendCapability,
} from './deviceVisuals';
import { deviceLabel } from './deviceNaming';
import { useTelemetryBatch } from '../charts/useTelemetryBatch';
import Sparkline from '../charts/Sparkline';
import { useDeviceProvenance } from './useDeviceProvenance';
import { describeProvenance } from './deviceProvenance';
import { useInViewport } from '../../hooks/useInViewport';
import { fmtRelativeShort } from '../../i18n/format';
import { zones as zonesResource } from '../../store/liveData';
import FlipCard from '../common/FlipCard';

/**
 * Homey-style device tile: an icon badge that glows when the device is "on", a headline state line, and a
 * quick on/off toggle for switchable devices. Enriched (roadmap dashboard fill, block C) with a lazy
 * sparkline of its primary numeric trend and a "last changed by …" chip. Tapping the body opens the drawer.
 */
export default function DeviceTile({
  device, onOpen, onCommand,
}: { device: CapabilityDevice; onOpen: (d: CapabilityDevice) => void; onCommand: CommandFn }) {
  const { t } = useTranslation('devices');
  const theme = useTheme();
  const { accent, Icon, isActive, primary, secondary } = describeDevice(device);
  const offline = !device.isOnline;

  const prim = primaryCapability(device);
  const quickToggle = prim?.id === 'on_off' && prim.writable;
  const on = asBool(device.state?.on_off);
  const brightness = 'brightness' in (device.state ?? {}) ? asNum(device.state.brightness) : undefined;
  const glow = isActive && !offline;

  // Sparkline: only for a numeric trend, and only fetched once the tile nears the viewport (lazy batch).
  const [ref, inView] = useInViewport<HTMLDivElement>();
  const trendCap = trendCapability(device);
  const { buckets } = useTelemetryBatch(device.id, trendCap, {
    enabled: inView && !!trendCap, hours: 24, bucket: 'hour', maxPoints: 24,
  });

  // Provenance chip: who last changed this device (batched across the grid).
  const lastEvent = useDeviceProvenance(device.id);
  const prov = lastEvent ? describeProvenance(lastEvent) : null;

  const face = (
    <Card
      ref={ref}
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
              <Tooltip title={offline ? t('status.offline') : t('status.online')}>
                <CircleIcon sx={{ fontSize: 10, mt: 1, color: offline ? 'text.disabled' : 'success.main' }} />
              </Tooltip>
            )}
          </Stack>

          <Box flex={1} minWidth={0}>
            <Typography variant="subtitle1" fontWeight={700} noWrap title={deviceLabel(device)}>
              {deviceLabel(device)}
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

          {trendCap && (
            <Sparkline buckets={buckets} color={glow ? accent : theme.palette.text.disabled} />
          )}

          {prov && (
            <Stack direction="row" alignItems="center" spacing={0.75} flexWrap="wrap" useFlexGap>
              <Chip
                size="small"
                label={prov.label}
                sx={{
                  height: 20, fontSize: '.66rem', fontWeight: 600,
                  bgcolor: prov.accented ? 'action.selected' : 'action.hover',
                  color: prov.accented ? 'primary.main' : 'text.secondary',
                  '& .MuiChip-label': { px: 0.75 },
                }}
              />
              <Typography variant="caption" color="text.secondary">
                {fmtRelativeShort(prov.when)}
              </Typography>
            </Stack>
          )}
        </Stack>
      </CardActionArea>
    </Card>
  );

  // Tiles without a numeric trend render exactly as before — no flip, no trigger.
  if (!trendCap) return face;

  return (
    <FlipCard
      front={face}
      back={<DeviceBack device={device} offline={offline} onOpen={onOpen} />}
      flipLabel={t('tile.details')}
      backLabel={t('common:flip.back')}
      // Top-right belongs to the quick toggle / status dot; the trigger sits bottom-right.
      triggerSx={{ top: 'auto', bottom: 4, right: 4 }}
      // Until the first flip the tile's DOM is byte-identical to the plain card — grids stay cheap
      // and text queries (device name, provenance chip) keep finding exactly one node.
      lazyBack
    />
  );
}

/** Flip side of a device tile: the passport rows the face has no room for. */
function DeviceBack({
  device, offline, onOpen,
}: { device: CapabilityDevice; offline: boolean; onOpen: (d: CapabilityDevice) => void }) {
  const { t } = useTranslation('devices');
  // Zone NAME, not the raw GUID; the shared resource is already loaded by any device page.
  const zoneList = zonesResource.use().data;
  const zoneName = device.zoneId
    ? (zoneList?.find((z) => z.id === device.zoneId)?.name ?? device.zoneId)
    : '—';
  return (
    // minHeight (not height): overflow must reach FlipCard's scroll container, not clip in the Card.
    <Card sx={{ minHeight: '100%', opacity: offline ? 0.55 : 1 }}>
      <CardContent sx={{ p: 2, height: '100%', '&:last-child': { pb: 2 } }}>
        <Stack height="100%" spacing={1}>
          <Typography variant="subtitle1" fontWeight={700} noWrap title={deviceLabel(device)}>
            {deviceLabel(device)}
          </Typography>
          <Stack spacing={0.25} flex={1}>
            <BackRow label={t('tile.model')} value={device.model || '—'} />
            <BackRow label={t('tile.adapter')} value={device.adapterSource} />
            <BackRow label={t('tile.zone')} value={zoneName} />
            <BackRow
              label={t('tile.lastSeen')}
              value={device.lastUpdated ? fmtRelativeShort(device.lastUpdated) : '—'}
            />
          </Stack>
          <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
            {device.capabilities.slice(0, 6).map((cap) => (
              <Chip
                key={cap.id}
                size="small"
                label={`${capabilityLabel(cap.id)}: ${formatCapabilityValue(cap, device.state?.[cap.id])}`}
                sx={{ height: 20, fontSize: '.66rem', '& .MuiChip-label': { px: 0.75 } }}
              />
            ))}
          </Stack>
          {/* pr clears the flip trigger in the bottom-right corner. */}
          <Button size="small" onClick={() => onOpen(device)} sx={{ alignSelf: 'flex-start', mr: 4 }}>
            {t('tile.open')}
          </Button>
        </Stack>
      </CardContent>
    </Card>
  );
}

function BackRow({ label, value }: { label: string; value: string }) {
  return (
    <Stack direction="row" spacing={1} alignItems="baseline">
      <Typography variant="caption" color="text.secondary" sx={{ minWidth: 76, flexShrink: 0 }}>
        {label}
      </Typography>
      <Typography variant="caption" noWrap>{value}</Typography>
    </Stack>
  );
}
