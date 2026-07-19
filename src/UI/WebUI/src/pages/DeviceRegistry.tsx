// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert, Box, Button, Chip, Container, Dialog, DialogActions, DialogContent,
  DialogContentText, DialogTitle, IconButton, InputAdornment, LinearProgress,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow,
  TextField, Tooltip, Typography,
} from '@mui/material';
import RefreshIcon from '@mui/icons-material/Refresh';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import DevicesIcon from '@mui/icons-material/Devices';
import {
  capabilityDevicesApi, CapabilityDevice, effectiveArchetype, isServiceDevice, isUnassignedZone,
} from '../api/capabilityDevices';
import { zonesApi, Zone } from '../api/zones';
import DeviceDetailDrawer from '../components/devices/DeviceDetailDrawer';
import type { CommandFn } from '../components/devices/CapabilityControls';
import { deviceCategory, capabilityIconForCategory, CATEGORY_ACCENT } from '../components/devices/deviceVisuals';
import { useUIStore } from '../store/uiStore';

const REFRESH_INTERVAL_MS = 20_000;

/**
 * The device registry: the full inventory as a dense table — every device the house knows,
 * physical, virtual and service alike. This is the admin counterpart of the home screen (which
 * only shows the lived-in house): service devices sit behind a default-off toggle, offline
 * devices can be pruned (delete is reversible — a re-announce re-discovers the device).
 */
export default function DeviceRegistry() {
  const { t, i18n } = useTranslation('devices');
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [zones, setZones] = useState<Zone[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [adapter, setAdapter] = useState<string>('all');
  const [onlineOnly, setOnlineOnly] = useState(false);
  const [showService, setShowService] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<CapabilityDevice | null>(null);
  const [deleting, setDeleting] = useState(false);

  const fetchDevices = useCallback(async () => {
    setError(null);
    try {
      setDevices(await capabilityDevicesApi.getDevices());
    } catch {
      setError(t('errors.loadDevices'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    fetchDevices();
    zonesApi.getZones().then(setZones).catch(() => {
      // Zones are optional grouping metadata; a failure just falls back to "Unassigned".
    });
    const interval = setInterval(fetchDevices, REFRESH_INTERVAL_MS);
    return () => clearInterval(interval);
  }, [fetchDevices]);

  const unassignedLabel = t('unassigned');
  const zoneName = useCallback(
    (zoneId?: string | null): string => {
      if (isUnassignedZone(zoneId)) return unassignedLabel;
      return zones.find((z) => z.id === zoneId)?.name ?? unassignedLabel;
    },
    [zones, unassignedLabel],
  );

  // Service devices hide first, so the adapter chip row only offers sources that are visible.
  const visible = useMemo(
    () => (showService ? devices : devices.filter((d) => !isServiceDevice(d))),
    [devices, showService],
  );
  const adapters = useMemo(
    () => Array.from(new Set(visible.map((d) => d.adapterSource))).sort(),
    [visible],
  );

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return visible
      .filter((d) =>
        (adapter === 'all' || d.adapterSource === adapter) &&
        (!onlineOnly || d.isOnline) &&
        (!q || d.name.toLowerCase().includes(q) || zoneName(d.zoneId).toLowerCase().includes(q)))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [visible, search, adapter, onlineOnly, zoneName]);

  const handleCommand = useCallback<CommandFn>((deviceId, set) => {
    setDevices((prev) =>
      prev.map((d) => (d.id === deviceId ? { ...d, state: { ...d.state, ...set } } : d)));
    capabilityDevicesApi.sendCommand(deviceId, set).catch(() => setError(t('errors.command')));
  }, [t]);

  const handleAssignZone = useCallback((deviceId: string, zoneId: string | null) => {
    const normalized = zoneId ?? '';
    setDevices((prev) => prev.map((d) => (d.id === deviceId ? { ...d, zoneId: normalized } : d)));
    capabilityDevicesApi.assignZone(deviceId, zoneId).catch(() => setError(t('errors.assignZone')));
  }, [t]);

  const handleSetArchetype = useCallback((deviceId: string, archetype: string | null) => {
    setDevices((prev) => prev.map((d) => (d.id === deviceId ? { ...d, archetype } : d)));
    capabilityDevicesApi.setArchetype(deviceId, archetype).catch(() => setError(t('errors.setArchetype')));
  }, [t]);

  const confirmDelete = useCallback(async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await capabilityDevicesApi.deleteDevice(deleteTarget.id);
      useUIStore.getState().showNotification('success', t('registry.deleted', { name: deleteTarget.name }));
      setDeleteTarget(null);
      await fetchDevices();
    } catch {
      setError(t('registry.deleteError'));
      setDeleteTarget(null);
    } finally {
      setDeleting(false);
    }
  }, [deleteTarget, fetchDevices, t]);

  const selected = useMemo(() => devices.find((d) => d.id === selectedId) ?? null, [devices, selectedId]);

  return (
    <Container maxWidth="xl">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="flex-start" justifyContent="space-between" mb={3} flexWrap="wrap" gap={2}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('registry.title')}</Typography>
            <Typography variant="body2" color="text.secondary" mt={0.5}>
              {t('registry.subtitle')}
            </Typography>
          </Box>
          <Tooltip title={t('actions.refresh')}>
            <span>
              <IconButton onClick={fetchDevices} disabled={loading}><RefreshIcon /></IconButton>
            </span>
          </Tooltip>
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        <Stack direction="row" spacing={1.5} mb={2} flexWrap="wrap" useFlexGap alignItems="center">
          <TextField
            size="small"
            placeholder={t('filters.searchPlaceholder')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            sx={{ minWidth: 240, flex: { xs: '1 1 100%', sm: '0 1 320px' } }}
            InputProps={{
              startAdornment: (
                <InputAdornment position="start"><SearchRoundedIcon fontSize="small" /></InputAdornment>
              ),
            }}
          />
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            <Chip
              label={t('filters.all')}
              variant={adapter === 'all' ? 'filled' : 'outlined'}
              color={adapter === 'all' ? 'primary' : 'default'}
              onClick={() => setAdapter('all')}
            />
            {adapters.map((a) => (
              <Chip
                key={a}
                label={a}
                variant={adapter === a ? 'filled' : 'outlined'}
                color={adapter === a ? 'primary' : 'default'}
                onClick={() => setAdapter(a)}
              />
            ))}
            <Chip
              label={t('filters.onlineOnly')}
              variant={onlineOnly ? 'filled' : 'outlined'}
              color={onlineOnly ? 'success' : 'default'}
              onClick={() => setOnlineOnly((v) => !v)}
            />
            <Tooltip title={t('registry.serviceHint')}>
              <Chip
                label={t('registry.serviceToggle')}
                variant={showService ? 'filled' : 'outlined'}
                color={showService ? 'secondary' : 'default'}
                onClick={() => {
                  setShowService((v) => !v);
                  setAdapter('all');
                }}
              />
            </Tooltip>
          </Stack>
          <Typography variant="caption" color="text.secondary" sx={{ ml: 'auto' }}>
            {t('registry.shown', { shown: filtered.length, total: devices.length })}
          </Typography>
        </Stack>

        {filtered.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <DevicesIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">
              {devices.length === 0 ? t('empty.noDevices') : t('empty.noMatches')}
            </Typography>
          </Box>
        ) : (
          <TableContainer>
            <Table size="small" sx={{ '& td, & th': { whiteSpace: 'nowrap' } }}>
              <TableHead>
                <TableRow>
                  <TableCell>{t('registry.columns.device')}</TableCell>
                  <TableCell>{t('zone')}</TableCell>
                  <TableCell>{t('registry.columns.source')}</TableCell>
                  <TableCell>{t('type.label')}</TableCell>
                  <TableCell>{t('registry.columns.status')}</TableCell>
                  <TableCell>{t('registry.columns.updated')}</TableCell>
                  <TableCell align="right" />
                </TableRow>
              </TableHead>
              <TableBody>
                {filtered.map((device) => {
                  const category = deviceCategory(device);
                  const Icon = capabilityIconForCategory(category);
                  return (
                    <TableRow
                      key={device.id}
                      hover
                      onClick={() => setSelectedId(device.id)}
                      sx={{ cursor: 'pointer' }}
                    >
                      <TableCell>
                        <Stack direction="row" spacing={1.25} alignItems="center">
                          <Icon fontSize="small" sx={{ color: CATEGORY_ACCENT[category] }} />
                          <Box>
                            <Typography variant="body2" fontWeight={600}>{device.name}</Typography>
                            {device.model && (
                              <Typography variant="caption" color="text.secondary">{device.model}</Typography>
                            )}
                          </Box>
                        </Stack>
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2" color={isUnassignedZone(device.zoneId) ? 'text.disabled' : 'text.primary'}>
                          {zoneName(device.zoneId)}
                        </Typography>
                      </TableCell>
                      <TableCell>
                        <Chip size="small" variant="outlined" label={device.adapterSource} />
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2" color="text.secondary">{effectiveArchetype(device)}</Typography>
                      </TableCell>
                      <TableCell>
                        <Chip
                          size="small"
                          label={device.isOnline ? t('status.online') : t('status.offline')}
                          color={device.isOnline ? 'success' : 'default'}
                          variant={device.isOnline ? 'filled' : 'outlined'}
                        />
                      </TableCell>
                      <TableCell>
                        <Typography variant="caption" color="text.secondary">
                          {new Date(device.lastUpdated).toLocaleString(i18n.language)}
                        </Typography>
                      </TableCell>
                      <TableCell align="right">
                        {!device.isOnline && (
                          <Tooltip title={t('registry.deleteHint')}>
                            <IconButton
                              size="small"
                              color="error"
                              aria-label={t('registry.delete', { name: device.name })}
                              onClick={(e) => { e.stopPropagation(); setDeleteTarget(device); }}
                            >
                              <DeleteOutlineRoundedIcon fontSize="small" />
                            </IconButton>
                          </Tooltip>
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </Box>

      <Dialog open={deleteTarget !== null} onClose={() => setDeleteTarget(null)}>
        <DialogTitle>{t('registry.deleteConfirmTitle')}</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {t('registry.deleteConfirm', { name: deleteTarget?.name })}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDeleteTarget(null)}>{t('geo.cancel')}</Button>
          <Button color="error" variant="contained" disabled={deleting} onClick={confirmDelete}>
            {t('registry.deleteAction')}
          </Button>
        </DialogActions>
      </Dialog>

      <DeviceDetailDrawer
        device={selected}
        zones={zones}
        open={selectedId !== null}
        onClose={() => setSelectedId(null)}
        onCommand={handleCommand}
        onAssignZone={handleAssignZone}
        onSetArchetype={handleSetArchetype}
      />
    </Container>
  );
}
