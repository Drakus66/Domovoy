import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Container, Box, Typography, Stack, Button, IconButton, LinearProgress, Alert,
  Card, CardContent, Tooltip, Chip, Dialog, DialogTitle, DialogContent, DialogActions,
  TextField, MenuItem, Divider,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import { blocksApi, BlockCatalogEntry, ControlBlock, NewBlock, PortBinding } from '../api/blocks';
import { capabilityDevicesApi, CapabilityDevice } from '../api/capabilityDevices';

const fmt = (v: unknown): string => {
  if (v === null || v === undefined || v === '') return '—';
  if (typeof v === 'boolean') return v ? 'on' : 'off';
  if (typeof v === 'number') return String(Math.round(v * 100) / 100);
  return String(v);
};

interface BlockDraft {
  name: string;
  typeId: string;
  params: Record<string, number>;
  inputs: Record<string, PortBinding>;
}

export default function Blocks() {
  const [blocks, setBlocks] = useState<ControlBlock[]>([]);
  const [catalog, setCatalog] = useState<BlockCatalogEntry[]>([]);
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [draft, setDraft] = useState<BlockDraft | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [b, c, d] = await Promise.all([
        blocksApi.getBlocks(), blocksApi.getCatalog(), capabilityDevicesApi.getDevices(),
      ]);
      setBlocks(b);
      setCatalog(c);
      setDevices(d);
    } catch {
      setError('Failed to load control blocks. Check ApiGateway / AutomationService connection.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  // Poll live output (blocks are virtual devices) so demand/setpoint update on screen.
  useEffect(() => {
    const t = setInterval(() => {
      capabilityDevicesApi.getDevices().then(setDevices).catch(() => undefined);
    }, 5000);
    return () => clearInterval(t);
  }, []);

  const deviceById = useMemo(() => new Map(devices.map((d) => [d.id, d])), [devices]);
  const typeById = useMemo(() => new Map(catalog.map((t) => [t.typeId, t])), [catalog]);

  const startCreate = (entry: BlockCatalogEntry) => {
    setDraft({
      name: '',
      typeId: entry.typeId,
      params: Object.fromEntries(entry.params.map((p) => [p.name, p.default])),
      inputs: Object.fromEntries(entry.inputs.map((p) => [p.name, { deviceId: '', capabilityId: '' }])),
    });
  };

  const save = async () => {
    if (!draft || !draft.name.trim()) return;
    // Drop unbound input ports (optional bindings).
    const inputs = Object.fromEntries(
      Object.entries(draft.inputs).filter(([, b]) => b.deviceId && b.capabilityId),
    );
    const payload: NewBlock = {
      name: draft.name.trim(), typeId: draft.typeId, enabled: true, params: draft.params, inputs,
    };
    try {
      await blocksApi.createBlock(payload);
      setDraft(null);
      await load();
    } catch {
      setError('Failed to create block.');
    }
  };

  const remove = async (b: ControlBlock) => {
    if (!window.confirm(`Delete block "${b.name}"? Its virtual device disappears.`)) return;
    try { await blocksApi.deleteBlock(b.id); await load(); }
    catch { setError('Failed to delete block.'); }
  };

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={1}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>Control blocks</Typography>
            <Typography variant="caption" color="text.secondary">
              Stateful loops between rules and devices — filters, thermostats, sequencers. Each block is a
              virtual device you can chart, wire and command.
            </Typography>
          </Box>
        </Stack>

        {/* Catalog — one "New" per built-in type (typed authoring, roadmap Epic 1H). */}
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mb={3}>
          {catalog.map((t) => (
            <Tooltip key={t.typeId} title={t.description}>
              <Button size="small" variant="outlined" startIcon={<AddRoundedIcon />} onClick={() => startCreate(t)}>
                {t.title}
              </Button>
            </Tooltip>
          ))}
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {blocks.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <AccountTreeRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">No control blocks yet. Add one from the catalog above.</Typography>
          </Box>
        ) : (
          <Stack spacing={1.5}>
            {blocks.map((b) => {
              const type = typeById.get(b.typeId);
              const vdev = deviceById.get(b.deviceId);
              return (
                <Card key={b.id} variant="outlined">
                  <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                    <Stack direction="row" alignItems="flex-start" spacing={2}>
                      <Box flex={1} minWidth={0}>
                        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.5}>
                          <Typography fontWeight={700}>{b.name}</Typography>
                          <Chip size="small" variant="outlined" label={type?.title ?? b.typeId} />
                          {!b.enabled && <Chip size="small" label="disabled" color="default" variant="outlined" />}
                        </Stack>

                        {Object.keys(b.inputs).length > 0 && (
                          <Typography variant="body2" color="text.secondary">
                            <b>Inputs</b>{' '}
                            {Object.entries(b.inputs).map(([port, bind]) =>
                              `${port} ← ${deviceById.get(bind.deviceId)?.name ?? bind.deviceId}.${bind.capabilityId}`).join(' · ')}
                          </Typography>
                        )}

                        <Typography variant="body2" color="text.secondary">
                          <b>Output</b>{' '}
                          {vdev && vdev.state && Object.keys(vdev.state).length > 0
                            ? Object.entries(vdev.state).map(([k, v]) => `${k}=${fmt(v)}`).join(' · ')
                            : <em>no samples yet</em>}
                        </Typography>
                      </Box>
                      <Tooltip title="Delete">
                        <IconButton onClick={() => remove(b)}><DeleteOutlineRoundedIcon /></IconButton>
                      </Tooltip>
                    </Stack>
                  </CardContent>
                </Card>
              );
            })}
          </Stack>
        )}
      </Box>

      <CreateDialog
        draft={draft} catalog={typeById} devices={devices}
        onChange={setDraft} onClose={() => setDraft(null)} onSave={save}
      />
    </Container>
  );
}

function CreateDialog({
  draft, catalog, devices, onChange, onClose, onSave,
}: {
  draft: BlockDraft | null;
  catalog: Map<string, BlockCatalogEntry>;
  devices: CapabilityDevice[];
  onChange: (d: BlockDraft) => void;
  onClose: () => void;
  onSave: () => void;
}) {
  const type = draft ? catalog.get(draft.typeId) : undefined;
  const capsOf = (deviceId: string) => devices.find((d) => d.id === deviceId)?.capabilities ?? [];

  return (
    <Dialog open={draft !== null} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>New {type?.title ?? 'block'}</DialogTitle>
      <DialogContent>
        {draft && type && (
          <Stack spacing={2.5} mt={1}>
            <Typography variant="caption" color="text.secondary">{type.description}</Typography>
            <TextField label="Name" value={draft.name} autoFocus required fullWidth
              onChange={(e) => onChange({ ...draft, name: e.target.value })} />

            {type.inputs.length > 0 && (
              <Box>
                <Typography variant="overline" color="text.secondary">Inputs (wiring)</Typography>
                {type.inputs.map((port) => {
                  const bind = draft.inputs[port.name] ?? { deviceId: '', capabilityId: '' };
                  return (
                    <Stack key={port.name} direction={{ xs: 'column', sm: 'row' }} spacing={1.5} mt={1}>
                      <TextField select label={`${port.name} · device`} value={bind.deviceId} fullWidth
                        helperText={port.description}
                        onChange={(e) => onChange({
                          ...draft,
                          inputs: { ...draft.inputs, [port.name]: { deviceId: e.target.value, capabilityId: '' } },
                        })}>
                        <MenuItem value=""><em>None</em></MenuItem>
                        {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
                      </TextField>
                      <TextField select label="capability" value={bind.capabilityId} fullWidth disabled={!bind.deviceId}
                        onChange={(e) => onChange({
                          ...draft,
                          inputs: { ...draft.inputs, [port.name]: { ...bind, capabilityId: e.target.value } },
                        })}>
                        {capsOf(bind.deviceId).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
                      </TextField>
                    </Stack>
                  );
                })}
              </Box>
            )}

            {type.params.length > 0 && (
              <Box>
                <Typography variant="overline" color="text.secondary">Parameters</Typography>
                <Stack spacing={1.5} mt={1}>
                  {type.params.map((p) => (
                    <TextField
                      key={p.name} type="number" label={`${p.name}${p.unit ? ` (${p.unit})` : ''}`}
                      value={draft.params[p.name] ?? p.default}
                      helperText={p.description}
                      onChange={(e) => onChange({
                        ...draft, params: { ...draft.params, [p.name]: Number(e.target.value) },
                      })}
                    />
                  ))}
                </Stack>
              </Box>
            )}

            <Divider />
            <Typography variant="caption" color="text.secondary">
              Outputs: {type.outputs.map((o) => `${o.id}${o.writable ? ' (writable)' : ''}`).join(', ')}
            </Typography>
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={onSave} disabled={!draft?.name.trim()}>Create</Button>
      </DialogActions>
    </Dialog>
  );
}
