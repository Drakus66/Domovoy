import { useEffect, useState, useCallback, useRef, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import { useNavigate, useParams } from 'react-router-dom';
import {
  Container, Box, Typography, IconButton, LinearProgress, Alert, Tooltip, Stack,
} from '@mui/material';
import RefreshIcon from '@mui/icons-material/Refresh';
import { HubConnection } from '@microsoft/signalr';
import { capabilityDevicesApi, CapabilityDevice, isUnassignedZone } from '../api/capabilityDevices';
import { zonesApi, Zone } from '../api/zones';
import type { Dashboard } from '../api/dashboards';
import { buildDeviceHubConnection, startDeviceHub } from '../api/deviceHub';
import DeviceDetailDrawer from '../components/devices/DeviceDetailDrawer';
import type { CommandFn } from '../components/devices/CapabilityControls';
import { asBool, asNum } from '../components/devices/deviceVisuals';
import DomovoyDigest from '../components/common/DomovoyDigest';
import AllDevicesTab from '../components/dashboard/AllDevicesTab';
import SphereTab from '../components/dashboard/SphereTab';
import CustomDashboardTab from '../components/dashboard/CustomDashboardTab';
import DashboardTabs from '../components/dashboard/DashboardTabs';
import DashboardEditorDialog from '../components/dashboard/editor/DashboardEditorDialog';
import { deriveSpheres, sphereCategoryFromTabId } from '../components/dashboard/spheres';
import { useDashboardStore } from '../store/dashboardStore';
import { useUIStore } from '../store/uiStore';

const REFRESH_INTERVAL_MS = 20_000;

export default function Devices() {
  const { t } = useTranslation('devices');
  const navigate = useNavigate();
  const { tabId } = useParams();
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [zones, setZones] = useState<Zone[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editorOpen, setEditorOpen] = useState(false);
  const [editorTarget, setEditorTarget] = useState<Dashboard | null>(null);
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

  // Custom dashboards + hidden-spheres preference (shared cache; also used by the editor).
  const {
    dashboards, hiddenSpheres, loaded: dashboardsLoaded,
    load: loadDashboards, reorder, setHiddenSpheres,
  } = useDashboardStore();
  useEffect(() => { loadDashboards(); }, [loadDashboards]);

  const spheres = useMemo(() => deriveSpheres(devices, hiddenSpheres), [devices, hiddenSpheres]);
  const allSpheres = useMemo(() => deriveSpheres(devices, []), [devices]);

  // Active tab: "all" (bare /), "sphere:<category>" or a dashboard id (route /t/:tabId).
  const activeTab = tabId ?? 'all';
  const sphereCategory = sphereCategoryFromTabId(activeTab);
  const activeSphere = sphereCategory !== null && spheres.some((s) => s.category === sphereCategory)
    ? sphereCategory
    : null;
  const activeDashboard = useMemo(
    () => dashboards.find((d) => d.id === activeTab) ?? null,
    [dashboards, activeTab],
  );

  // A deep link to a deleted dashboard / hidden or empty sphere falls back to All —
  // but only once both devices and dashboards have actually loaded.
  useEffect(() => {
    if (loading || !dashboardsLoaded) return;
    if (activeTab !== 'all' && !activeSphere && !activeDashboard) {
      navigate('/', { replace: true });
    }
  }, [loading, dashboardsLoaded, activeTab, activeSphere, activeDashboard, navigate]);

  const selectTab = useCallback(
    (id: string) => navigate(id === 'all' ? '/' : `/t/${id}`),
    [navigate],
  );

  const openEditor = useCallback((target: Dashboard | null) => {
    setEditorTarget(target);
    setEditorOpen(true);
  }, []);

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

        <DashboardTabs
          spheres={spheres}
          allSpheres={allSpheres}
          dashboards={dashboards}
          hiddenSpheres={hiddenSpheres}
          activeId={activeDashboard ? activeDashboard.id : activeSphere ? `sphere:${activeSphere}` : 'all'}
          onSelect={selectTab}
          onCreate={() => openEditor(null)}
          onEdit={openEditor}
          onToggleSphere={(category, hidden) =>
            setHiddenSpheres(hidden
              ? [...hiddenSpheres, category]
              : hiddenSpheres.filter((c) => c !== category))}
          onReorder={reorder}
        />

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {activeDashboard ? (
          <CustomDashboardTab
            dashboard={activeDashboard}
            devices={devices}
            onOpen={(d) => setSelectedId(d.id)}
            onCommand={handleCommand}
            onEdit={() => openEditor(activeDashboard)}
          />
        ) : activeSphere ? (
          <SphereTab
            category={activeSphere}
            devices={devices}
            zoneName={zoneName}
            onOpen={(d) => setSelectedId(d.id)}
            onCommand={handleCommand}
          />
        ) : (
          <AllDevicesTab
            devices={devices}
            zoneName={zoneName}
            loading={loading}
            onOpen={(d) => setSelectedId(d.id)}
            onCommand={handleCommand}
          />
        )}
      </Box>

      <DashboardEditorDialog
        open={editorOpen}
        dashboard={editorTarget}
        devices={devices}
        onClose={() => setEditorOpen(false)}
        onSaved={(d) => selectTab(d.id)}
        onDeleted={(id) => { if (activeTab === id) selectTab('all'); }}
      />

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
