// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle,
  InputAdornment, List, ListItemButton, ListItemIcon, ListItemText, MenuItem,
  Radio, Select, Stack, TextField, ToggleButton, ToggleButtonGroup, Typography,
} from '@mui/material';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import DevicesOtherRoundedIcon from '@mui/icons-material/DevicesOtherRounded';
import SensorsRoundedIcon from '@mui/icons-material/SensorsRounded';
import ShowChartRoundedIcon from '@mui/icons-material/ShowChartRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import type { CapabilityDevice } from '../../../api/capabilityDevices';
import type { DashboardItem, DashboardItemType } from '../../../api/dashboards';
import { capabilityLabel, describeDevice } from '../../devices/deviceVisuals';

/** Chart window presets → aggregation bucket (mirrors the device drawer's trend presets). */
const CHART_WINDOWS: Record<number, 'minute' | 'hour' | 'day'> = { 6: 'minute', 24: 'hour', 168: 'day' };

const TYPE_ICONS: Record<DashboardItemType, JSX.Element> = {
  device: <DevicesOtherRoundedIcon />,
  capability: <SensorsRoundedIcon />,
  chart: <ShowChartRoundedIcon />,
  modes: <TuneRoundedIcon />,
};

/**
 * Two-step item picker for the tab editor: choose what to add, then which device/value.
 * `device` supports multi-add (checkboxes); `capability`/`chart` are one device + one value;
 * `modes` needs nothing. Returns finished DashboardItems via onAdd.
 */
export default function AddItemDialog({
  open, devices, onClose, onAdd,
}: {
  open: boolean;
  devices: CapabilityDevice[];
  onClose: () => void;
  onAdd: (items: DashboardItem[]) => void;
}) {
  const { t } = useTranslation('dashboards');
  const [type, setType] = useState<DashboardItemType | null>(null);
  const [search, setSearch] = useState('');
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [deviceId, setDeviceId] = useState<string | null>(null);
  const [capabilityId, setCapabilityId] = useState('');
  const [hours, setHours] = useState(24);

  const reset = () => {
    setType(null);
    setSearch('');
    setSelectedIds(new Set());
    setDeviceId(null);
    setCapabilityId('');
    setHours(24);
  };

  const close = () => { reset(); onClose(); };

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    const list = q ? devices.filter((d) => d.name.toLowerCase().includes(q)) : devices;
    return [...list].sort((a, b) => a.name.localeCompare(b.name));
  }, [devices, search]);

  const selectedDevice = deviceId ? devices.find((d) => d.id === deviceId) ?? null : null;
  // Charts aggregate numbers; a capability tile can show any value.
  const capabilityOptions = useMemo(() => {
    if (!selectedDevice) return [];
    return type === 'chart'
      ? selectedDevice.capabilities.filter((c) => c.kind === 'Number')
      : selectedDevice.capabilities;
  }, [selectedDevice, type]);

  const canAdd =
    type === 'modes' ||
    (type === 'device' && selectedIds.size > 0) ||
    ((type === 'capability' || type === 'chart') && deviceId !== null && capabilityId !== '');

  const add = () => {
    if (!type) return;
    if (type === 'modes') {
      onAdd([{ type: 'modes' }]);
    } else if (type === 'device') {
      onAdd(Array.from(selectedIds).map((id) => ({ type: 'device' as const, deviceId: id })));
    } else {
      const item: DashboardItem = { type, deviceId, capabilityId };
      if (type === 'chart') item.params = { hours, bucket: CHART_WINDOWS[hours] ?? 'hour' };
      onAdd([item]);
    }
    close();
  };

  const pickDevice = (id: string) => {
    if (type === 'device') {
      setSelectedIds((prev) => {
        const next = new Set(prev);
        if (next.has(id)) next.delete(id); else next.add(id);
        return next;
      });
    } else {
      setDeviceId(id);
      setCapabilityId('');
    }
  };

  return (
    <Dialog open={open} onClose={close} fullWidth maxWidth="xs">
      <DialogTitle>{type ? t(`editor.types.${type}`) : t('editor.pickType')}</DialogTitle>
      <DialogContent dividers sx={{ minHeight: 320 }}>
        {type === null && (
          <List>
            {(['device', 'capability', 'chart', 'modes'] as DashboardItemType[]).map((option) => (
              <ListItemButton key={option} onClick={() => setType(option)} sx={{ borderRadius: 2 }}>
                <ListItemIcon>{TYPE_ICONS[option]}</ListItemIcon>
                <ListItemText
                  primary={t(`editor.types.${option}`)}
                  secondary={t(`editor.typeHints.${option}`)}
                />
              </ListItemButton>
            ))}
          </List>
        )}

        {type === 'modes' && (
          <Typography variant="body2" color="text.secondary">{t('editor.typeHints.modes')}</Typography>
        )}

        {(type === 'device' || type === 'capability' || type === 'chart') && (
          <Stack spacing={1.5}>
            <Typography variant="body2" color="text.secondary">
              {type === 'device' ? t('editor.pickDevices') : t('editor.pickDevice')}
            </Typography>
            <TextField
              size="small"
              placeholder={t('editor.searchDevices')}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              InputProps={{
                startAdornment: (
                  <InputAdornment position="start"><SearchRoundedIcon fontSize="small" /></InputAdornment>
                ),
              }}
            />
            <List dense sx={{ maxHeight: 260, overflow: 'auto' }}>
              {filtered.map((device) => {
                const { Icon } = describeDevice(device);
                const checked = type === 'device' ? selectedIds.has(device.id) : deviceId === device.id;
                return (
                  <ListItemButton key={device.id} onClick={() => pickDevice(device.id)} sx={{ borderRadius: 2 }}>
                    <ListItemIcon sx={{ minWidth: 34 }}>
                      {type === 'device'
                        ? <Checkbox edge="start" size="small" checked={checked} disableRipple tabIndex={-1} />
                        : <Radio edge="start" size="small" checked={checked} disableRipple tabIndex={-1} />}
                    </ListItemIcon>
                    <ListItemIcon sx={{ minWidth: 34, color: 'text.secondary' }}><Icon fontSize="small" /></ListItemIcon>
                    <ListItemText primary={device.name} secondary={device.model || device.adapterSource} />
                  </ListItemButton>
                );
              })}
            </List>

            {(type === 'capability' || type === 'chart') && selectedDevice && (
              <Box>
                <Typography variant="body2" color="text.secondary" mb={0.5}>
                  {t('editor.pickCapability')}
                </Typography>
                <Select
                  size="small" fullWidth value={capabilityId} displayEmpty
                  onChange={(e) => setCapabilityId(e.target.value)}
                >
                  {capabilityOptions.map((c) => (
                    <MenuItem key={c.id} value={c.id}>{capabilityLabel(c.id)}</MenuItem>
                  ))}
                </Select>
              </Box>
            )}

            {type === 'chart' && (
              <Box>
                <Typography variant="body2" color="text.secondary" mb={0.5}>
                  {t('editor.chartWindow')}
                </Typography>
                <ToggleButtonGroup
                  size="small" exclusive value={hours}
                  onChange={(_, v) => { if (v !== null) setHours(v); }}
                >
                  {Object.keys(CHART_WINDOWS).map((h) => (
                    <ToggleButton key={h} value={Number(h)}>{t(`editor.window.${h}`)}</ToggleButton>
                  ))}
                </ToggleButtonGroup>
              </Box>
            )}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        {type !== null && <Button onClick={reset}>{t('actions.back')}</Button>}
        <Box flex={1} />
        <Button onClick={close}>{t('actions.cancel')}</Button>
        <Button variant="contained" disabled={!canAdd} onClick={add}>{t('actions.add')}</Button>
      </DialogActions>
    </Dialog>
  );
}
