import { useEffect, useState, useCallback, useRef, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import {
  Container, Box, Typography, Grid, IconButton, LinearProgress, Alert, Tooltip,
  Stack, TextField, InputAdornment, Chip, Divider,
} from '@mui/material';
import RefreshIcon from '@mui/icons-material/Refresh';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import DevicesIcon from '@mui/icons-material/Devices';
import { HubConnection } from '@microsoft/signalr';
import { capabilityDevicesApi, CapabilityDevice, isUnassignedZone } from '../api/capabilityDevices';
import { zonesApi, Zone } from '../api/zones';
import { buildDeviceHubConnection, startDeviceHub } from '../api/deviceHub';
import DeviceTile from '../components/devices/DeviceTile';
import DeviceDetailDrawer from '../components/devices/DeviceDetailDrawer';
import type { CommandFn } from '../components/devices/CapabilityControls';
import { asBool, asNum } from '../components/devices/deviceVisuals';
import DomovoyDigest from '../components/common/DomovoyDigest';
import { useUIStore } from '../store/uiStore';

const REFRESH_INTERVAL_MS = 20_000;

export default function Devices() {
  const { t } = useTranslation('devices');
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [zones, setZones] = useState<Zone[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [adapter, setAdapter] = useState<string>('all');
  const [onlineOnly, setOnlineOnly] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const hubRef = useRef<HubConnection | null>(null);
  // Device ids seen by the last successful fetch; null until the baseline load,
  // so restarts don't announce the whole house as "new residents".
  const knownIdsRef = useRef<Set<string> | null>(null);

  const fetchDevices = useCallback(async () => {
    setError(null);
    try {
      const list = await capabilityDevicesApi.getDevices();
      const known = knownIdsRef.current;
      if (known) {
        for (const d of list) {
          if (!known.has(d.id)) {
            useUIStore.getState().showNotification('info', i18n.t('newResident', { name: d.name }));
          }
        }
      }
      knownIdsRef.current = new Set(list.map((d) => d.id));
      setDevices(list);
    } catch {
      setError(t('errors.loadDevices'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  const fetchZones = useCallback(async () => {
    try {
      setZones(await zonesApi.getZones());
    } catch {
      // Zones are optional grouping metadata; a failure just falls back to raw ids / Unassigned.
    }
  }, []);

  // Initial load + periodic refresh (picks up newly discovered devices / capabilities).
  useEffect(() => {
    fetchDevices();
    fetchZones();
    const interval = setInterval(fetchDevices, REFRESH_INTERVAL_MS);
    return () => clearInterval(interval);
  }, [fetchDevices, fetchZones]);

  // Resolve a device's zone id to its display name; blank/unknown → "Unassigned".
  const unassignedLabel = t('unassigned');
  const zoneName = useCallback(
    (zoneId?: string | null): string => {
      if (isUnassignedZone(zoneId)) return unassignedLabel;
      return zones.find((z) => z.id === zoneId)?.name ?? unassignedLabel;
    },
    [zones, unassignedLabel],
  );

  // Live state via SignalR (normalized capability state, keyed by device GUID).
  useEffect(() => {
    let cancelled = false;
    const conn = buildDeviceHubConnection();

    conn.on('DeviceStateUpdated', (deviceId: string, state: Record<string, unknown>) => {
      setDevices((prev) =>
        prev.map((d) =>
          d.id === deviceId ? { ...d, state: { ...d.state, ...state }, isOnline: true } : d
        )
      );
    });

    conn.on('DeviceDiscovered', () => { fetchDevices(); });
    conn.onclose(() => { if (!cancelled) startDeviceHub(conn, () => cancelled); });

    startDeviceHub(conn, () => cancelled);
    hubRef.current = conn;
    return () => { cancelled = true; conn.stop(); };
  }, [fetchDevices]);

  const handleCommand = useCallback<CommandFn>((deviceId, set) => {
    // Optimistic update so the control reflects intent immediately.
    setDevices((prev) =>
      prev.map((d) => (d.id === deviceId ? { ...d, state: { ...d.state, ...set } } : d))
    );
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

  const adapters = useMemo(
    () => Array.from(new Set(devices.map((d) => d.adapterSource))).sort(),
    [devices],
  );

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return devices.filter((d) =>
      (adapter === 'all' || d.adapterSource === adapter) &&
      (!onlineOnly || d.isOnline) &&
      (!q || d.name.toLowerCase().includes(q) || zoneName(d.zoneId).toLowerCase().includes(q)),
    );
  }, [devices, search, adapter, onlineOnly, zoneName]);

  // Group by zone name, with stable ordering (Unassigned last).
  const grouped = useMemo(() => {
    const map = new Map<string, CapabilityDevice[]>();
    for (const d of filtered) {
      const key = zoneName(d.zoneId);
      const bucket = map.get(key) ?? [];
      bucket.push(d);
      map.set(key, bucket);
    }
    return Array.from(map.entries())
      .sort(([a], [b]) => (a === unassignedLabel ? 1 : b === unassignedLabel ? -1 : a.localeCompare(b)))
      .map(([zone, items]) => ({ zone, items: items.sort((x, y) => x.name.localeCompare(y.name)) }));
  }, [filtered, zoneName, unassignedLabel]);

  const onlineCount = devices.filter((d) => d.isOnline).length;
  const lightsOn = devices.filter((d) => 'on_off' in (d.state ?? {}) && asBool(d.state.on_off)
    && d.capabilities.some((c) => c.id === 'brightness')).length;
  const totalPower = devices.reduce((sum, d) => sum + ('power' in (d.state ?? {}) ? asNum(d.state.power) : 0), 0);

  const selected = useMemo(() => devices.find((d) => d.id === selectedId) ?? null, [devices, selectedId]);

  return (
    <Container maxWidth="xl">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="flex-start" justifyContent="space-between" mb={3} flexWrap="wrap" gap={2}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <DomovoyDigest />
            <Stack direction="row" spacing={1} mt={0.5} flexWrap="wrap" useFlexGap>
              <Stat label={t('stats.online')} value={`${onlineCount}/${devices.length}`} />
              {lightsOn > 0 && <Stat label={t('stats.lightsOn')} value={String(lightsOn)} />}
              {totalPower > 0 && <Stat label={t('stats.power')} value={`${Math.round(totalPower)} W`} />}
            </Stack>
          </Box>
          <Tooltip title={t('actions.refresh')}>
            <span>
              <IconButton onClick={fetchDevices} disabled={loading}><RefreshIcon /></IconButton>
            </span>
          </Tooltip>
        </Stack>

        <Stack direction="row" spacing={1.5} mb={3} flexWrap="wrap" useFlexGap alignItems="center">
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
          </Stack>
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {filtered.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <DevicesIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">
              {devices.length === 0
                ? t('empty.noDevices')
                : t('empty.noMatches')}
            </Typography>
          </Box>
        ) : (
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
                      <DeviceTile device={device} onOpen={(d) => setSelectedId(d.id)} onCommand={handleCommand} />
                    </Grid>
                  ))}
                </Grid>
              </Box>
            ))}
          </Stack>
        )}
      </Box>

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

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <Typography variant="caption" color="text.secondary">
      <Box component="span" sx={{ color: 'text.primary', fontWeight: 700 }}>{value}</Box> {label}
    </Typography>
  );
}
