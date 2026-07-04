import { useEffect, useState, useCallback, useRef } from 'react';
import {
  Container, Box, Typography, Card, CardContent, CardActions,
  Chip, IconButton, Button, TextField, Dialog, DialogTitle,
  DialogContent, DialogActions, Alert, LinearProgress, Tooltip,
  Grid, Divider, Slider, Switch, FormControlLabel, CircularProgress,
} from '@mui/material';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import RefreshIcon from '@mui/icons-material/Refresh';
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/Delete';
import BluetoothSearchingIcon from '@mui/icons-material/BluetoothSearching';
import RouterIcon from '@mui/icons-material/Router';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import ErrorIcon from '@mui/icons-material/Error';
import LightbulbIcon from '@mui/icons-material/Lightbulb';
import SensorsIcon from '@mui/icons-material/Sensors';
import PowerIcon from '@mui/icons-material/Power';
import DevicesIcon from '@mui/icons-material/Devices';
import SignalCellularAltIcon from '@mui/icons-material/SignalCellularAlt';
import { HubConnection } from '@microsoft/signalr';
import { zigbeeApi, ZigbeeBridge, ZigbeeDevice } from '../api/zigbee';
import { buildDeviceHubConnection, startDeviceHub } from '../api/deviceHub';

const PERMIT_JOIN_DURATIONS = [30, 60, 120, 254];
const REFRESH_INTERVAL_MS = 30_000;

function deviceIcon(type: string, description: string) {
  const desc = description.toLowerCase();
  if (desc.includes('light') || desc.includes('bulb')) return <LightbulbIcon fontSize="small" />;
  if (desc.includes('sensor') || desc.includes('temperature') || desc.includes('humidity'))
    return <SensorsIcon fontSize="small" />;
  if (desc.includes('switch') || desc.includes('plug')) return <PowerIcon fontSize="small" />;
  if (type === 'Router') return <RouterIcon fontSize="small" />;
  return <DevicesIcon fontSize="small" />;
}

function linkQualityColor(lq?: number): 'success' | 'warning' | 'error' {
  if (!lq) return 'error';
  if (lq >= 150) return 'success';
  if (lq >= 80) return 'warning';
  return 'error';
}

function PermitJoinTimer({ active, initialSeconds }: { active: boolean; initialSeconds: number }) {
  const [remaining, setRemaining] = useState(initialSeconds);

  useEffect(() => {
    setRemaining(initialSeconds);
    if (!active || initialSeconds <= 0) return;
    const id = setInterval(() => setRemaining((s) => Math.max(0, s - 1)), 1000);
    return () => clearInterval(id);
  }, [active, initialSeconds]);

  if (!active) return null;
  const mm = String(Math.floor(remaining / 60)).padStart(2, '0');
  const ss = String(remaining % 60).padStart(2, '0');
  return (
    <Chip
      icon={<BluetoothSearchingIcon />}
      label={i18n.t('zigbee:pairing.chip', { time: `${mm}:${ss}` })}
      color="warning"
      variant="filled"
      size="small"
      sx={{ animation: 'pulse 1s infinite', '@keyframes pulse': { '0%,100%': { opacity: 1 }, '50%': { opacity: 0.6 } } }}
    />
  );
}

function DeviceCard({
  device,
  onRename,
  onRemove,
  onControl,
}: {
  device: ZigbeeDevice;
  onRename: (d: ZigbeeDevice) => void;
  onRemove: (d: ZigbeeDevice) => void;
  onControl: (d: ZigbeeDevice, updates: Record<string, unknown>) => void;
}) {
  const { t } = useTranslation('zigbee');
  const { state } = device;
  const isLight = device.description.toLowerCase().includes('light') || device.description.toLowerCase().includes('bulb');
  const isSwitch = device.description.toLowerCase().includes('switch') || device.description.toLowerCase().includes('plug');
  const isOn = state.state === 'ON';

  return (
    <Card variant="outlined" sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
      <CardContent sx={{ flex: 1 }}>
        <Box display="flex" alignItems="flex-start" gap={1} mb={1}>
          <Box sx={{ color: 'text.secondary', mt: 0.3 }}>
            {deviceIcon(device.type, device.description)}
          </Box>
          <Box flex={1} minWidth={0}>
            <Typography variant="subtitle2" fontWeight={600} noWrap title={device.friendlyName}>
              {device.friendlyName}
            </Typography>
            <Typography variant="caption" color="text.secondary" display="block" noWrap>
              {device.vendor} {device.model}
            </Typography>
          </Box>
          {state.linkquality !== undefined && (
            <Tooltip title={t('linkQuality', { value: state.linkquality })}>
              <Box sx={{ color: `${linkQualityColor(state.linkquality as number)}.main`, display: 'flex' }}>
                <SignalCellularAltIcon fontSize="small" />
              </Box>
            </Tooltip>
          )}
        </Box>

        {/* State indicators */}
        <Box display="flex" flexWrap="wrap" gap={0.5} mb={1}>
          {state.temperature !== undefined && (
            <Chip label={`${state.temperature}°C`} size="small" variant="outlined" />
          )}
          {state.humidity !== undefined && (
            <Chip label={`${state.humidity}%`} size="small" variant="outlined" />
          )}
          {state.battery !== undefined && (
            <Chip label={`🔋 ${state.battery}%`} size="small" variant="outlined"
              color={(state.battery as number) < 20 ? 'error' : 'default'} />
          )}
          {state.occupancy !== undefined && (
            <Chip label={state.occupancy ? t('state.occupied') : t('state.empty')} size="small" variant="outlined" />
          )}
          {state.contact !== undefined && (
            <Chip label={state.contact ? t('state.closed') : t('state.open')} size="small" variant="outlined" />
          )}
        </Box>

        {/* Light / Switch controls */}
        {(isLight || isSwitch) && state.state !== undefined && (
          <Box>
            <FormControlLabel
              control={
                <Switch
                  checked={isOn}
                  onChange={(e) => onControl(device, { state: e.target.checked ? 'ON' : 'OFF' })}
                  size="small"
                  color="warning"
                />
              }
              label={<Typography variant="body2">{isOn ? t('state.on') : t('state.off')}</Typography>}
            />
            {isLight && isOn && state.brightness !== undefined && (
              <Box px={1}>
                <Typography variant="caption" color="text.secondary">
                  {t('brightness', { value: Math.round(((state.brightness as number) / 254) * 100) })}
                </Typography>
                <Slider
                  size="small"
                  min={1}
                  max={254}
                  value={state.brightness as number}
                  onChange={(_, v) => onControl(device, { brightness: v as number })}
                  sx={{ color: 'warning.main' }}
                />
              </Box>
            )}
          </Box>
        )}
      </CardContent>

      <Divider />
      <CardActions sx={{ px: 1, py: 0.5, justifyContent: 'space-between' }}>
        <Typography variant="caption" color="text.secondary">
          {device.ieeeAddress}
        </Typography>
        <Box>
          <Tooltip title={t('device.rename')}>
            <IconButton size="small" onClick={() => onRename(device)}>
              <EditIcon fontSize="small" />
            </IconButton>
          </Tooltip>
          <Tooltip title={t('device.remove')}>
            <IconButton size="small" color="error" onClick={() => onRemove(device)}>
              <DeleteIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        </Box>
      </CardActions>
    </Card>
  );
}

export default function ZigbeeDevices() {
  const { t } = useTranslation('zigbee');
  const [bridge, setBridge] = useState<ZigbeeBridge | null>(null);
  const [devices, setDevices] = useState<ZigbeeDevice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [permitJoinDuration, setPermitJoinDuration] = useState(254);
  const [permitJoinActive, setPermitJoinActive] = useState(false);
  const [permitJoinRemaining, setPermitJoinRemaining] = useState(0);
  const [renameTarget, setRenameTarget] = useState<ZigbeeDevice | null>(null);
  const [renameValue, setRenameValue] = useState('');
  const [removeTarget, setRemoveTarget] = useState<ZigbeeDevice | null>(null);
  const [actionLoading, setActionLoading] = useState(false);
  const [notification, setNotification] = useState<{ type: 'success' | 'info' | 'error'; text: string } | null>(null);
  const hubRef = useRef<HubConnection | null>(null);

  const fetchData = useCallback(async () => {
    setError(null);
    try {
      const [b, d] = await Promise.all([zigbeeApi.getBridge(), zigbeeApi.getDevices()]);
      setBridge(b);
      setDevices(d);
      setPermitJoinActive(b.permitJoin);
      setPermitJoinRemaining(b.permitJoinTimeout);
    } catch {
      setError(t('loadError'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, REFRESH_INTERVAL_MS);
    return () => clearInterval(interval);
  }, [fetchData]);

  useEffect(() => {
    let cancelled = false;
    const conn = buildDeviceHubConnection();

    conn.on('ZigbeeBridgeStateChanged', (isOnline: boolean) => {
      setBridge((prev) => prev ? { ...prev, isOnline } : prev);
    });

    conn.on('ZigbeeBridgeInfoUpdated', (info: Partial<ZigbeeBridge>) => {
      setBridge((prev) => prev ? { ...prev, ...info } : prev);
      if (info.permitJoin !== undefined) {
        setPermitJoinActive(info.permitJoin);
        setPermitJoinRemaining(info.permitJoinTimeout ?? 0);
      }
    });

    conn.on('ZigbeeNetworkEvent', (eventType: string, friendlyName: string) => {
      if (eventType === 'device_joined') {
        setNotification({ type: 'success', text: t('events.joined', { name: friendlyName }) });
        fetchData();
      } else if (eventType === 'device_leave') {
        setNotification({ type: 'info', text: t('events.left', { name: friendlyName }) });
        fetchData();
      }
    });

    // If reconnection ultimately gives up (or never engaged), re-establish from scratch.
    conn.onclose(() => { if (!cancelled) startDeviceHub(conn, () => cancelled); });

    startDeviceHub(conn, () => cancelled);
    hubRef.current = conn;
    return () => { cancelled = true; conn.stop(); };
  }, [fetchData, t]);

  const handlePermitJoin = async (duration: number) => {
    setActionLoading(true);
    try {
      await zigbeeApi.permitJoin(duration);
      setPermitJoinActive(duration > 0);
      setPermitJoinRemaining(duration);
      setNotification({ type: duration > 0 ? 'success' : 'info', text: duration > 0 ? t('pairing.enabled', { duration }) : t('pairing.disabled') });
    } catch {
      setNotification({ type: 'error', text: t('pairing.error') });
    } finally {
      setActionLoading(false);
    }
  };

  const handleRenameConfirm = async () => {
    if (!renameTarget || !renameValue.trim()) return;
    setActionLoading(true);
    try {
      await zigbeeApi.renameDevice(renameTarget.friendlyName, renameValue.trim());
      setNotification({ type: 'success', text: t('renameDialog.success', { name: renameValue.trim() }) });
      setRenameTarget(null);
      setTimeout(fetchData, 1000);
    } catch {
      setNotification({ type: 'error', text: t('renameDialog.error') });
    } finally {
      setActionLoading(false);
    }
  };

  const handleRemoveConfirm = async () => {
    if (!removeTarget) return;
    setActionLoading(true);
    try {
      await zigbeeApi.removeDevice(removeTarget.friendlyName);
      setNotification({ type: 'success', text: t('removeDialog.success', { name: removeTarget.friendlyName }) });
      setRemoveTarget(null);
      setTimeout(fetchData, 1500);
    } catch {
      setNotification({ type: 'error', text: t('removeDialog.error') });
    } finally {
      setActionLoading(false);
    }
  };

  const handleDeviceControl = async (device: ZigbeeDevice, updates: Record<string, unknown>) => {
    try {
      await import('../api/client').then(({ default: client }) =>
        client.post(`/api/zigbee/devices/${encodeURIComponent(device.friendlyName)}/set`, updates)
      );
      setDevices((prev) =>
        prev.map((d) =>
          d.ieeeAddress === device.ieeeAddress
            ? { ...d, state: { ...d.state, ...updates } }
            : d
        )
      );
    } catch {
      setNotification({ type: 'error', text: t('commandError') });
    }
  };

  const onlineCount = devices.filter((d) => {
    const age = new Date().getTime() - new Date(d.lastSeen).getTime();
    return age < 10 * 60 * 1000;
  }).length;

  return (
    <Container maxWidth="xl">
      <Box py={4}>

        {/* Header */}
        <Box display="flex" alignItems="center" justifyContent="space-between" mb={3} flexWrap="wrap" gap={2}>
          <Box>
            <Typography variant="h4" fontWeight={600}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t('subtitle')}
            </Typography>
          </Box>
          <Box display="flex" alignItems="center" gap={1}>
            <PermitJoinTimer active={permitJoinActive} initialSeconds={permitJoinRemaining} />
            <Tooltip title={t('refresh')}>
              <span>
                <IconButton onClick={fetchData} disabled={loading}>
                  <RefreshIcon />
                </IconButton>
              </span>
            </Tooltip>
          </Box>
        </Box>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
        {notification && (
          <Alert severity={notification.type} sx={{ mb: 2 }} onClose={() => setNotification(null)}>
            {notification.text}
          </Alert>
        )}

        {/* Bridge info card */}
        <Card variant="outlined" sx={{ mb: 3 }}>
          <CardContent>
            <Grid container spacing={2} alignItems="center">
              <Grid item xs={12} sm="auto">
                <Box display="flex" alignItems="center" gap={1}>
                  <Box sx={{ color: bridge?.isOnline ? 'success.main' : 'error.main' }}>
                    {bridge?.isOnline ? <CheckCircleIcon /> : <ErrorIcon />}
                  </Box>
                  <Typography variant="subtitle1" fontWeight={600}>
                    {bridge?.isOnline ? t('bridge.online') : t('bridge.offline')}
                  </Typography>
                </Box>
              </Grid>
              {bridge && (
                <>
                  <Grid item xs={6} sm="auto">
                    <Typography variant="caption" color="text.secondary" display="block">{t('bridge.coordinator')}</Typography>
                    <Typography variant="body2">{bridge.coordinator.type || '—'}</Typography>
                  </Grid>
                  <Grid item xs={6} sm="auto">
                    <Typography variant="caption" color="text.secondary" display="block">{t('bridge.channel')}</Typography>
                    <Typography variant="body2">{bridge.network.channel || '—'}</Typography>
                  </Grid>
                  <Grid item xs={6} sm="auto">
                    <Typography variant="caption" color="text.secondary" display="block">{t('bridge.panId')}</Typography>
                    <Typography variant="body2">{bridge.network.panId || '—'}</Typography>
                  </Grid>
                  <Grid item xs={6} sm="auto">
                    <Typography variant="caption" color="text.secondary" display="block">{t('bridge.version')}</Typography>
                    <Typography variant="body2">{bridge.version || '—'}</Typography>
                  </Grid>
                  <Grid item xs={6} sm="auto">
                    <Typography variant="caption" color="text.secondary" display="block">{t('bridge.devices')}</Typography>
                    <Typography variant="body2">{t('bridge.devicesSummary', { total: devices.length, recent: onlineCount })}</Typography>
                  </Grid>
                </>
              )}
              <Grid item xs={12} sm="auto" sx={{ ml: { sm: 'auto' } }}>
                <Box display="flex" alignItems="center" gap={1}>
                  <Typography variant="body2" color="text.secondary">{t('pairing.label')}</Typography>
                  {PERMIT_JOIN_DURATIONS.map((d) => (
                    <Button
                      key={d}
                      size="small"
                      variant={permitJoinActive && d === permitJoinDuration ? 'contained' : 'outlined'}
                      color="warning"
                      onClick={() => { setPermitJoinDuration(d); handlePermitJoin(d); }}
                      disabled={actionLoading}
                      sx={{ minWidth: 48 }}
                    >
                      {d}s
                    </Button>
                  ))}
                  {permitJoinActive && (
                    <Button size="small" variant="outlined" color="error"
                      onClick={() => handlePermitJoin(0)} disabled={actionLoading}>
                      {t('pairing.stop')}
                    </Button>
                  )}
                </Box>
              </Grid>
            </Grid>
          </CardContent>
        </Card>

        {/* Device grid */}
        {devices.length === 0 && !loading ? (
          <Box textAlign="center" py={6}>
            <BluetoothSearchingIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">
              {t('empty')}
            </Typography>
          </Box>
        ) : (
          <Grid container spacing={2}>
            {devices.map((device) => (
              <Grid item xs={12} sm={6} md={4} lg={3} key={device.ieeeAddress}>
                <DeviceCard
                  device={device}
                  onRename={(d) => { setRenameTarget(d); setRenameValue(d.friendlyName); }}
                  onRemove={setRemoveTarget}
                  onControl={handleDeviceControl}
                />
              </Grid>
            ))}
          </Grid>
        )}

      </Box>

      {/* Rename dialog */}
      <Dialog open={!!renameTarget} onClose={() => setRenameTarget(null)} fullWidth maxWidth="xs">
        <DialogTitle>{t('renameDialog.title')}</DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" mb={2}>
            {t('renameDialog.currentName')} <strong>{renameTarget?.friendlyName}</strong>
          </Typography>
          <TextField
            autoFocus
            fullWidth
            label={t('renameDialog.newName')}
            value={renameValue}
            onChange={(e) => setRenameValue(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleRenameConfirm()}
            size="small"
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRenameTarget(null)}>{t('renameDialog.cancel')}</Button>
          <Button
            variant="contained"
            onClick={handleRenameConfirm}
            disabled={actionLoading || !renameValue.trim()}
          >
            {actionLoading ? <CircularProgress size={18} /> : t('renameDialog.confirm')}
          </Button>
        </DialogActions>
      </Dialog>

      {/* Remove confirm dialog */}
      <Dialog open={!!removeTarget} onClose={() => setRemoveTarget(null)} fullWidth maxWidth="xs">
        <DialogTitle>{t('removeDialog.title')}</DialogTitle>
        <DialogContent>
          <Alert severity="warning" sx={{ mb: 1 }}>
            {t('removeDialog.warning', { name: removeTarget?.friendlyName })}
          </Alert>
          <Typography variant="body2" color="text.secondary">
            {t('removeDialog.note')}
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRemoveTarget(null)}>{t('removeDialog.cancel')}</Button>
          <Button
            variant="contained"
            color="error"
            onClick={handleRemoveConfirm}
            disabled={actionLoading}
          >
            {actionLoading ? <CircularProgress size={18} /> : t('removeDialog.confirm')}
          </Button>
        </DialogActions>
      </Dialog>

    </Container>
  );
}
