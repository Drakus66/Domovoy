// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState, useCallback, useRef, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  Container, Box, Typography, IconButton, LinearProgress, Alert, Tooltip, Stack,
} from '@mui/material';
import RefreshIcon from '@mui/icons-material/Refresh';
import CategoryRoundedIcon from '@mui/icons-material/CategoryRounded';
import { HubConnection } from '@microsoft/signalr';
import { capabilityDevicesApi, CapabilityDevice, isUnassignedZone, isServiceDevice } from '../api/capabilityDevices';
import { mlApi, ArchetypeDisagreement } from '../api/ml';
import { zonesApi, Zone } from '../api/zones';
import type { Dashboard } from '../api/dashboards';
import { buildDeviceHubConnection, startDeviceHub } from '../api/deviceHub';
import DeviceDetailDrawer from '../components/devices/DeviceDetailDrawer';
import type { CommandFn } from '../components/devices/CapabilityControls';
import { asBool, asNum } from '../components/devices/deviceVisuals';
import { deviceLabel, proposeZoneName } from '../components/devices/deviceNaming';
import { useDeviceRename } from '../components/devices/useDeviceRename';
import HomeStateBand from '../components/dashboard/HomeStateBand';
import DomovoyRail from '../components/dashboard/DomovoyRail';
import OverviewTab from '../components/dashboard/OverviewTab';
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
  const [searchParams, setSearchParams] = useSearchParams();
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [zones, setZones] = useState<Zone[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editorOpen, setEditorOpen] = useState(false);
  const [editorTarget, setEditorTarget] = useState<Dashboard | null>(null);
  // ML device-type review (Epic 2D, lives here since 2P): run the classifier, list disagreements.
  const [classifying, setClassifying] = useState(false);
  const [classifyInfo, setClassifyInfo] = useState<string | null>(null);
  const [disagreements, setDisagreements] = useState<ArchetypeDisagreement[] | null>(null);
  const hubRef = useRef<HubConnection | null>(null);

  // Deep-link from attribution chips (Epic 2G tail): /?device={id} opens the device drawer directly.
  useEffect(() => {
    const focus = searchParams.get('device');
    if (!focus) return;
    setSelectedId(focus);
    setSearchParams((p) => { p.delete('device'); return p; }, { replace: true });
  }, [searchParams, setSearchParams]);
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
            useUIStore.getState().showNotification('info', i18n.t('newResident', { name: deviceLabel(d) }));
          }
        }
      }
      knownIdsRef.current = new Set(list.map((d) => d.id));
      setDevices(list);
    } catch {
      // i18n.t, а не хук t: иначе fetchDevices зависит от t, а от fetchDevices зависит эффект
      // SignalR — и смена языка рвала и переустанавливала хаб устройств.
      setError(i18n.t('devices:errors.loadDevices'));
    } finally {
      setLoading(false);
    }
  }, []);

  const classify = useCallback(async () => {
    setClassifying(true); setClassifyInfo(null); setDisagreements(null);
    try {
      const r = await mlApi.classifyArchetypes();
      if (!r.trained) {
        setClassifyInfo(t('classify.notTrained', { note: r.note }));
      } else {
        setDisagreements(r.disagreements);
        setClassifyInfo(t('classify.done', { trainedOn: r.trainedOn, count: r.disagreements.length }));
      }
    } catch {
      setClassifyInfo(t('classify.error'));
    } finally {
      setClassifying(false);
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

  // Rename-on-zone flow (Epic 3G): applied aliases are reflected in local state.
  const { dialog: renameDialog, requestRename } = useDeviceRename((updates) => {
    setDevices((prev) => prev.map((d) => (d.id in updates ? { ...d, alias: updates[d.id] } : d)));
  });

  const handleAssignZone = useCallback((deviceId: string, zoneId: string | null) => {
    const normalized = zoneId ?? '';
    const device = devices.find((d) => d.id === deviceId);
    setDevices((prev) => prev.map((d) => (d.id === deviceId ? { ...d, zoneId: normalized } : d)));
    capabilityDevicesApi.assignZone(deviceId, zoneId).catch(() => setError(t('errors.assignZone')));
    // Offer to fold the zone into the device's name ("Люстра" → "Люстра в Гостиная").
    if (device) {
      const newZoneName = zoneId ? (zones.find((z) => z.id === zoneId)?.name ?? null) : null;
      const current = deviceLabel(device);
      requestRename([{ device, current, proposed: proposeZoneName(current, newZoneName, zones.map((z) => z.name)) }]);
    }
  }, [devices, zones, requestRename, t]);

  const handleSetAlias = useCallback((deviceId: string, alias: string | null) => {
    setDevices((prev) => prev.map((d) => (d.id === deviceId ? { ...d, alias } : d)));
    capabilityDevicesApi.setAlias(deviceId, alias).catch(() => setError(t('errors.setAlias')));
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

  // The home screen shows the lived-in house: service devices (System virtual sensors, block
  // projections) are registry material (/devices) and stay off the tabs. Custom dashboards still
  // receive the full list — anything can be pinned there, service devices included.
  const homeDevices = useMemo(() => devices.filter((d) => !isServiceDevice(d)), [devices]);

  const spheres = useMemo(() => deriveSpheres(homeDevices, hiddenSpheres), [homeDevices, hiddenSpheres]);
  const allSpheres = useMemo(() => deriveSpheres(homeDevices, []), [homeDevices]);

  // Active tab: "overview" (bare /), "sphere:<category>" or a dashboard id (route /t/:tabId).
  const activeTab = tabId ?? 'overview';
  const sphereCategory = sphereCategoryFromTabId(activeTab);
  const activeSphere = sphereCategory !== null && spheres.some((s) => s.category === sphereCategory)
    ? sphereCategory
    : null;
  const activeDashboard = useMemo(
    () => dashboards.find((d) => d.id === activeTab) ?? null,
    [dashboards, activeTab],
  );

  // A deep link to a deleted dashboard / hidden or empty sphere (or the retired "all" tab)
  // falls back to Overview — but only once both devices and dashboards have actually loaded.
  useEffect(() => {
    if (loading || !dashboardsLoaded) return;
    if (activeTab !== 'overview' && !activeSphere && !activeDashboard) {
      navigate('/', { replace: true });
    }
  }, [loading, dashboardsLoaded, activeTab, activeSphere, activeDashboard, navigate]);

  const selectTab = useCallback(
    (id: string) => navigate(id === 'overview' ? '/' : `/t/${id}`),
    [navigate],
  );

  const openEditor = useCallback((target: Dashboard | null) => {
    setEditorTarget(target);
    setEditorOpen(true);
  }, []);

  // Header stats count the lived-in house only — service devices are always "online" and would
  // just pad the numbers.
  const onlineCount = homeDevices.filter((d) => d.isOnline).length;
  const lightsOn = homeDevices.filter((d) => 'on_off' in (d.state ?? {}) && asBool(d.state.on_off)
    && d.capabilities.some((c) => c.id === 'brightness')).length;
  const totalPower = homeDevices.reduce((sum, d) => sum + ('power' in (d.state ?? {}) ? asNum(d.state.power) : 0), 0);

  const selected = useMemo(() => devices.find((d) => d.id === selectedId) ?? null, [devices, selectedId]);

  return (
    <Container maxWidth="xl">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="flex-start" justifyContent="space-between" mb={3} flexWrap="wrap" gap={2}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Stack direction="row" spacing={1} mt={0.5} flexWrap="wrap" useFlexGap>
              <Stat label={t('stats.online')} value={`${onlineCount}/${devices.length}`} />
              {lightsOn > 0 && <Stat label={t('stats.lightsOn')} value={String(lightsOn)} />}
              {totalPower > 0 && <Stat label={t('stats.power')} value={`${Math.round(totalPower)} W`} />}
            </Stack>
          </Box>
          <Stack direction="row" spacing={0.5} alignItems="center">
            {/* ML device-type review (Epic 2D, moved here from the ML page in 2P — it is about devices). */}
            <Tooltip title={t('classify.actionHint')}>
              <span>
                <IconButton onClick={classify} disabled={classifying} aria-label={t('classify.action')}>
                  <CategoryRoundedIcon />
                </IconButton>
              </span>
            </Tooltip>
            <Tooltip title={t('actions.refresh')}>
              <span>
                <IconButton onClick={fetchDevices} disabled={loading}><RefreshIcon /></IconButton>
              </span>
            </Tooltip>
          </Stack>
        </Stack>

        {classifyInfo && (
          <Alert severity="info" sx={{ mb: 2 }} onClose={() => setClassifyInfo(null)}>{classifyInfo}</Alert>
        )}
        {disagreements && disagreements.length > 0 && (
          <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setDisagreements(null)}>
            <Typography variant="subtitle2" gutterBottom>{t('classify.reviewTitle')}</Typography>
            <Stack spacing={0.5}>
              {disagreements.map((d) => (
                <Typography key={d.deviceId} variant="body2">
                  {d.name}: {d.current} → <b>{d.predicted}</b> ({(d.confidence * 100).toFixed(0)}%)
                </Typography>
              ))}
            </Stack>
          </Alert>
        )}

        <DashboardTabs
          spheres={spheres}
          allSpheres={allSpheres}
          dashboards={dashboards}
          hiddenSpheres={hiddenSpheres}
          activeId={activeDashboard ? activeDashboard.id : activeSphere ? `sphere:${activeSphere}` : 'overview'}
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

        <HomeStateBand devices={devices} />

        <Box sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, gap: 3, alignItems: 'flex-start' }}>
          <Box sx={{ flex: 1, minWidth: 0, width: '100%' }}>
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
                devices={homeDevices}
                zoneName={zoneName}
                onOpen={(d) => setSelectedId(d.id)}
                onCommand={handleCommand}
              />
            ) : (
              <OverviewTab
                devices={homeDevices}
                zoneName={zoneName}
                loading={loading}
                onOpen={(d) => setSelectedId(d.id)}
                onCommand={handleCommand}
              />
            )}
          </Box>
          <DomovoyRail devices={devices} />
        </Box>
      </Box>

      <DashboardEditorDialog
        open={editorOpen}
        dashboard={editorTarget}
        devices={devices}
        onClose={() => setEditorOpen(false)}
        onSaved={(d) => selectTab(d.id)}
        onDeleted={(id) => { if (activeTab === id) selectTab('overview'); }}
      />

      <DeviceDetailDrawer
        device={selected}
        zones={zones}
        open={selectedId !== null}
        onClose={() => setSelectedId(null)}
        onCommand={handleCommand}
        onAssignZone={handleAssignZone}
        onSetArchetype={handleSetArchetype}
        onSetAlias={handleSetAlias}
      />

      {renameDialog}
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
