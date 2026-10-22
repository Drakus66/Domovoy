// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useEffect, useState, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  Container, Box, Typography, IconButton, LinearProgress, Alert, Tooltip, Stack,
} from '@mui/material';
import RefreshIcon from '@mui/icons-material/Refresh';
import CategoryRoundedIcon from '@mui/icons-material/CategoryRounded';
import { isServiceDevice } from '../api/capabilityDevices';
import { mlApi, ArchetypeDisagreement } from '../api/ml';
import type { Dashboard } from '../api/dashboards';
import DeviceDetailDrawer from '../components/devices/DeviceDetailDrawer';
import { asBool, asNum } from '../components/devices/deviceVisuals';
import { useDeviceCollection } from '../components/devices/useDeviceCollection';
import HomeStateBand from '../components/dashboard/HomeStateBand';
import DomovoyRail from '../components/dashboard/DomovoyRail';
import OverviewTab from '../components/dashboard/OverviewTab';
import SphereTab from '../components/dashboard/SphereTab';
import CustomDashboardTab from '../components/dashboard/CustomDashboardTab';
import DashboardTabs from '../components/dashboard/DashboardTabs';
import DashboardEditorDialog from '../components/dashboard/editor/DashboardEditorDialog';
import { deriveSpheres, sphereCategoryFromTabId } from '../components/dashboard/spheres';
import { useDashboardStore } from '../store/dashboardStore';


export default function Devices() {
  const { t } = useTranslation('devices');
  const navigate = useNavigate();
  const { tabId } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  // Список, зоны, живое состояние и действия над устройством — общие с реестром (/devices):
  // раньше обе страницы несли построчно одинаковые ~120 строк и ДВА разных списка одного дома.
  const {
    devices, zones, loading, error, setError, refresh,
    zoneName, handleCommand, handleAssignZone, handleSetAlias, handleSetArchetype, renameDialog,
  } = useDeviceCollection();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editorOpen, setEditorOpen] = useState(false);
  const [editorTarget, setEditorTarget] = useState<Dashboard | null>(null);
  // ML device-type review (Epic 2D, lives here since 2P): run the classifier, list disagreements.
  const [classifying, setClassifying] = useState(false);
  const [classifyInfo, setClassifyInfo] = useState<string | null>(null);
  const [disagreements, setDisagreements] = useState<ArchetypeDisagreement[] | null>(null);

  // Deep-link from attribution chips (Epic 2G tail): /?device={id} opens the device drawer directly.
  useEffect(() => {
    const focus = searchParams.get('device');
    if (!focus) return;
    setSelectedId(focus);
    setSearchParams((p) => { p.delete('device'); return p; }, { replace: true });
  }, [searchParams, setSearchParams]);

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
                <IconButton onClick={refresh} disabled={loading}><RefreshIcon /></IconButton>
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
