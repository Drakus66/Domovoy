import { useCallback, useEffect, useState } from 'react';
import {
  Container, Box, Typography, Stack, Button, LinearProgress, Alert, Card, CardContent,
  Chip, Tooltip,
} from '@mui/material';
import ExtensionRoundedIcon from '@mui/icons-material/ExtensionRounded';
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded';
import StopRoundedIcon from '@mui/icons-material/StopRounded';
import MemoryRoundedIcon from '@mui/icons-material/MemoryRounded';
import { pluginsApi, Plugin, PluginStatus, HostResources } from '../api/plugins';

const STATUS_COLOR: Record<PluginStatus, 'success' | 'warning' | 'error' | 'info' | 'default'> = {
  Running: 'success', Starting: 'info', Blocked: 'warning', Failed: 'error',
  Disabled: 'default', Stopped: 'default', Discovered: 'default',
};

const reqText = (r: Plugin['resources']): string => {
  const parts: string[] = [];
  if (r.cpuCores) parts.push(`${r.cpuCores} cores`);
  if (r.memoryMb) parts.push(`${r.memoryMb} MB`);
  if (r.gpu) parts.push('GPU');
  if (r.internet) parts.push('internet');
  return parts.length ? parts.join(' · ') : 'no special resources';
};

export default function Plugins() {
  const [plugins, setPlugins] = useState<Plugin[]>([]);
  const [host, setHost] = useState<HostResources | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const res = await pluginsApi.getPlugins();
      setPlugins(res.plugins);
      setHost(res.host);
    } catch {
      setError('Failed to load plugins. Check ApiGateway / PluginSupervisor connection.');
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
      setError(`Failed to ${action} ${p.name}.`);
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
            <Typography variant="h4" component="h1" fontWeight={700}>Plugins</Typography>
            <Typography variant="caption" color="text.secondary">
              Out-of-process integrations over the bus. Each declares the resources it needs; the supervisor
              runs only those the host can satisfy, isolated from the core.
            </Typography>
          </Box>
        </Stack>

        {host && (
          <Chip
            icon={<MemoryRoundedIcon />} variant="outlined" sx={{ mb: 2 }}
            label={`Host: ${host.cpuCores} cores · ${host.memoryMb} MB · GPU ${host.gpu ? 'yes' : 'no'} · internet ${host.internet ? 'yes' : 'no'}`}
          />
        )}

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {plugins.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <ExtensionRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">
              No plugins discovered. Drop a folder with a <code>plugin.json</code> into the plugins root.
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
                      <Typography variant="caption" color="text.secondary">v{p.version}</Typography>
                      <Chip size="small" variant="outlined" color={STATUS_COLOR[p.status]} label={p.status} />
                    </Stack>
                    {p.description && (
                      <Typography variant="body2" color="text.secondary">{p.description}</Typography>
                    )}
                    <Typography variant="caption" color="text.secondary">
                      Needs {reqText(p.resources)}
                      {p.providedCapabilities.length > 0 && ` · provides ${p.providedCapabilities.join(', ')}`}
                      {p.detail ? ` · ${p.detail}` : ''}
                    </Typography>
                  </Box>
                  {canStart(p.status) && (
                    <Tooltip title="Start">
                      <span>
                        <Button size="small" startIcon={<PlayArrowRoundedIcon />} disabled={busy === p.id}
                          onClick={() => act(p, 'start')}>Start</Button>
                      </span>
                    </Tooltip>
                  )}
                  {canStop(p.status) && (
                    <Tooltip title="Stop">
                      <span>
                        <Button size="small" color="warning" startIcon={<StopRoundedIcon />} disabled={busy === p.id}
                          onClick={() => act(p, 'stop')}>Stop</Button>
                      </span>
                    </Tooltip>
                  )}
                </CardContent>
              </Card>
            ))}
          </Stack>
        )}
      </Box>
    </Container>
  );
}
