import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import {
  Container, Box, Typography, Stack, Button, LinearProgress, Alert, Card, CardContent,
  Chip, Tooltip,
} from '@mui/material';
import ExtensionRoundedIcon from '@mui/icons-material/ExtensionRounded';
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded';
import StopRoundedIcon from '@mui/icons-material/StopRounded';
import MemoryRoundedIcon from '@mui/icons-material/MemoryRounded';
import UploadRoundedIcon from '@mui/icons-material/UploadRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded';
import { pluginsApi, Plugin, PluginStatus, HostResources } from '../api/plugins';
import PluginSettingsDialog from '../components/plugins/PluginSettingsDialog';

const STATUS_COLOR: Record<PluginStatus, 'success' | 'warning' | 'error' | 'info' | 'default'> = {
  Running: 'success', Starting: 'info', Blocked: 'warning', Failed: 'error',
  Disabled: 'default', Stopped: 'default', Discovered: 'default',
};

const reqText = (r: Plugin['resources']): string => {
  const parts: string[] = [];
  if (r.cpuCores) parts.push(i18n.t('plugins:resources.cores', { count: r.cpuCores }));
  if (r.memoryMb) parts.push(i18n.t('plugins:resources.memoryMb', { memoryMb: r.memoryMb }));
  if (r.gpu) parts.push(i18n.t('plugins:resources.gpu'));
  if (r.internet) parts.push(i18n.t('plugins:resources.internet'));
  return parts.length ? parts.join(' · ') : i18n.t('plugins:resources.none');
};

export default function Plugins() {
  const { t } = useTranslation('plugins');
  const [plugins, setPlugins] = useState<Plugin[]>([]);
  const [host, setHost] = useState<HostResources | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [installing, setInstalling] = useState(false);
  const [settingsFor, setSettingsFor] = useState<Plugin | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const res = await pluginsApi.getPlugins();
      setPlugins(res.plugins);
      setHost(res.host);
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);
  // Status changes as processes start/stop/crash — refresh periodically.
  useEffect(() => {
    const t = setInterval(load, 5000);
    return () => clearInterval(t);
  }, [load]);

  const act = async (p: Plugin, action: 'start' | 'stop') => {
    setBusy(p.id);
    try {
      await (action === 'start' ? pluginsApi.start(p.id) : pluginsApi.stop(p.id));
      await load();
    } catch {
      setError(t(action === 'start' ? 'errors.start' : 'errors.stop', { name: p.name }));
    } finally {
      setBusy(null);
    }
  };

  const onPickFile = () => fileInputRef.current?.click();

  const onFileSelected = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ''; // allow re-selecting the same file
    if (!file) return;

    setInstalling(true);
    setError(null);
    setInfo(null);
    try {
      const res = await pluginsApi.install(file);
      setInfo(t('install.success', { name: res.plugin?.name ?? file.name }));
      await load();
    } catch (err: unknown) {
      const detail =
        (err as { response?: { data?: { result?: string } } })?.response?.data?.result;
      setError(detail ? t('install.failedDetail', { detail }) : t('errors.install'));
    } finally {
      setInstalling(false);
    }
  };

  const uninstall = async (p: Plugin) => {
    if (!window.confirm(t('uninstall.confirm', { name: p.name }))) return;
    setBusy(p.id);
    setError(null);
    try {
      await pluginsApi.uninstall(p.id);
      setInfo(t('uninstall.success', { name: p.name }));
      await load();
    } catch {
      setError(t('errors.uninstall', { name: p.name }));
    } finally {
      setBusy(null);
    }
  };

  const canStart = (s: PluginStatus) => s === 'Stopped' || s === 'Failed' || s === 'Discovered';
  const canStop = (s: PluginStatus) => s === 'Running' || s === 'Starting';

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={1}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t('subtitle')}
            </Typography>
          </Box>
          <Tooltip title={t('install.hint')}>
            <span>
              <Button
                variant="contained" startIcon={<UploadRoundedIcon />}
                disabled={installing} onClick={onPickFile}
              >
                {installing ? t('install.installing') : t('install.button')}
              </Button>
            </span>
          </Tooltip>
          <input
            ref={fileInputRef} type="file" accept=".zip,application/zip"
            hidden onChange={onFileSelected}
          />
        </Stack>

        {host && (
          <Chip
            icon={<MemoryRoundedIcon />} variant="outlined" sx={{ mb: 2 }}
            label={t('host', {
              cores: host.cpuCores,
              memoryMb: host.memoryMb,
              gpu: host.gpu ? t('yes') : t('no'),
              internet: host.internet ? t('yes') : t('no'),
            })}
          />
        )}

        {(loading || installing) && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
        {info && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setInfo(null)}>{info}</Alert>}

        {plugins.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <ExtensionRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">
              {t('empty')}
            </Typography>
          </Box>
        ) : (
          <Stack spacing={1.5}>
            {plugins.map((p) => (
              <Card key={p.id} variant="outlined">
                <CardContent sx={{ display: 'flex', alignItems: 'center', gap: 2, py: 1.5, '&:last-child': { pb: 1.5 } }}>
                  <ExtensionRoundedIcon color="primary" />
                  <Box flex={1} minWidth={0}>
                    <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.25}>
                      <Typography fontWeight={700}>{p.name}</Typography>
                      <Typography variant="caption" color="text.secondary">{t('version', { version: p.version })}</Typography>
                      <Chip size="small" variant="outlined" color={STATUS_COLOR[p.status]} label={t(`status.${p.status}`)} />
                    </Stack>
                    {p.description && (
                      <Typography variant="body2" color="text.secondary">{p.description}</Typography>
                    )}
                    <Typography variant="caption" color="text.secondary">
                      {t('needs', { resources: reqText(p.resources) })}
                      {p.providedCapabilities.length > 0 && t('provides', { capabilities: p.providedCapabilities.join(', ') })}
                      {p.detail ? ` · ${p.detail}` : ''}
                    </Typography>
                  </Box>
                  {canStart(p.status) && (
                    <Tooltip title={t('actions.start')}>
                      <span>
                        <Button size="small" startIcon={<PlayArrowRoundedIcon />} disabled={busy === p.id}
                          onClick={() => act(p, 'start')}>{t('actions.start')}</Button>
                      </span>
                    </Tooltip>
                  )}
                  {canStop(p.status) && (
                    <Tooltip title={t('actions.stop')}>
                      <span>
                        <Button size="small" color="warning" startIcon={<StopRoundedIcon />} disabled={busy === p.id}
                          onClick={() => act(p, 'stop')}>{t('actions.stop')}</Button>
                      </span>
                    </Tooltip>
                  )}
                  {p.hasSettings && (
                    <Tooltip title={t('settings.button')}>
                      <span>
                        <Button size="small" startIcon={<SettingsRoundedIcon />}
                          onClick={() => setSettingsFor(p)}>{t('settings.button')}</Button>
                      </span>
                    </Tooltip>
                  )}
                  <Tooltip title={t('actions.uninstall')}>
                    <span>
                      <Button size="small" color="error" startIcon={<DeleteOutlineRoundedIcon />} disabled={busy === p.id}
                        onClick={() => uninstall(p)}>{t('actions.uninstall')}</Button>
                    </span>
                  </Tooltip>
                </CardContent>
              </Card>
            ))}
          </Stack>
        )}
      </Box>

      {settingsFor && (
        <PluginSettingsDialog
          open={!!settingsFor}
          pluginId={settingsFor.id}
          pluginName={settingsFor.name}
          onClose={() => { setSettingsFor(null); load(); }}
        />
      )}
    </Container>
  );
}
