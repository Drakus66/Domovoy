import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import {
  Container, Box, Typography, Stack, Button, IconButton, LinearProgress, Alert,
  Card, CardContent, Tooltip, Chip, Dialog, DialogTitle, DialogContent, DialogActions,
  TextField, MenuItem, Divider, ToggleButtonGroup, ToggleButton,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import AccountTreeRoundedIcon from '@mui/icons-material/AccountTreeRounded';
import ViewListRoundedIcon from '@mui/icons-material/ViewListRounded';
import PublishRoundedIcon from '@mui/icons-material/PublishRounded';
import { blocksApi, BlockCatalogEntry, ControlBlock, NewBlock, PortBinding } from '../api/blocks';
import { capabilityDevicesApi, CapabilityDevice } from '../api/capabilityDevices';
import { proposalsApi } from '../api/proposals';
import BlockGraph from '../components/blocks/BlockGraph';

const stageName = (s: number) =>
  i18n.t(s >= 2 ? 'blocks:stageName.full' : s === 1 ? 'blocks:stageName.bounded' : 'blocks:stageName.shadow');

// The authority ladder phrased as trust in the house spirit (2B staging):
// it starts by watching, then acts carefully, then runs the loop on its own.
const stageHint = (s: number) =>
  i18n.t(s >= 2 ? 'blocks:stageHint.full' : s === 1 ? 'blocks:stageHint.bounded' : 'blocks:stageHint.shadow');

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
  outputs: Record<string, PortBinding>;
}

export default function Blocks() {
  const { t } = useTranslation('blocks');
  const [blocks, setBlocks] = useState<ControlBlock[]>([]);
  const [catalog, setCatalog] = useState<BlockCatalogEntry[]>([]);
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [draft, setDraft] = useState<BlockDraft | null>(null);
  const [view, setView] = useState<'list' | 'graph'>('list');

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
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, [t]);

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
      outputs: Object.fromEntries(entry.outputs.map((o) => [o.id, { deviceId: '', capabilityId: '' }])),
    });
  };

  const save = async () => {
    if (!draft || !draft.name.trim()) return;
    // Drop unbound input/output ports (bindings are optional).
    const inputs = Object.fromEntries(
      Object.entries(draft.inputs).filter(([, b]) => b.deviceId && b.capabilityId),
    );
    const outputs = Object.fromEntries(
      Object.entries(draft.outputs).filter(([, b]) => b.deviceId && b.capabilityId),
    );
    const payload: NewBlock = {
      name: draft.name.trim(), typeId: draft.typeId, enabled: true, params: draft.params, inputs, outputs,
    };
    try {
      await blocksApi.createBlock(payload);
      setDraft(null);
      await load();
    } catch {
      setError(t('errors.create'));
    }
  };

  const remove = async (b: ControlBlock) => {
    if (!window.confirm(t('confirmDelete', { name: b.name }))) return;
    try { await blocksApi.deleteBlock(b.id); await load(); }
    catch { setError(t('errors.delete')); }
  };

  // Promotion goes through the approval queue (Epic 2C), not a direct stage edit — a human approves the
  // Shadow → Bounded → Full step after reading the scorecard.
  const proposePromotion = async (b: ControlBlock) => {
    const current = Math.round(b.params.stage ?? 0);
    const next = Math.min(2, current + 1);
    setError(null); setInfo(null);
    try {
      await proposalsApi.create({
        kind: 'BlockPromotion',
        title: t('promote.title', { name: b.name, from: stageName(current), to: stageName(next) }),
        blockId: b.id,
        fromStage: current,
        toStage: next,
        source: 'user',
        rationale: t('promote.rationale'),
      });
      setInfo(t('promote.queued', { name: b.name }));
    } catch {
      setError(t('errors.promote'));
    }
  };

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
          <ToggleButtonGroup size="small" exclusive value={view} onChange={(_, v) => v && setView(v)}>
            <ToggleButton value="list"><ViewListRoundedIcon fontSize="small" sx={{ mr: 0.5 }} />{t('view.list')}</ToggleButton>
            <ToggleButton value="graph"><AccountTreeRoundedIcon fontSize="small" sx={{ mr: 0.5 }} />{t('view.graph')}</ToggleButton>
          </ToggleButtonGroup>
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
        {info && <Alert severity="info" sx={{ mb: 2 }} onClose={() => setInfo(null)}>{info}</Alert>}

        {view === 'graph' ? (
          <BlockGraph blocks={blocks} devices={devices} />
        ) : blocks.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <AccountTreeRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">
              {t('empty')}
            </Typography>
          </Box>
        ) : (
          <Stack spacing={1.5}>
            {blocks.map((b) => {
              const type = typeById.get(b.typeId);
              const vdev = deviceById.get(b.deviceId);
              // ML governor blocks carry a `stage` param; below Full they can be promoted via the queue.
              const isGovernor = type?.params.some((p) => p.name === 'stage') ?? false;
              const stage = Math.round(b.params.stage ?? 0);
              return (
                <Card key={b.id} variant="outlined">
                  <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                    <Stack direction="row" alignItems="flex-start" spacing={2}>
                      <Box flex={1} minWidth={0}>
                        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.5}>
                          <Typography fontWeight={700}>{b.name}</Typography>
                          <Chip size="small" variant="outlined" label={type?.title ?? b.typeId} />
                          {!b.enabled && <Chip size="small" label={t('chip.disabled')} color="default" variant="outlined" />}
                          {isGovernor && (
                            <Tooltip title={stageHint(stage)}>
                              <Chip size="small" variant="outlined" color={stage === 0 ? 'default' : 'primary'}
                                label={t('chip.stage', { stage: stageName(stage) })} />
                            </Tooltip>
                          )}
                        </Stack>

                        {Object.keys(b.inputs).length > 0 && (
                          <Typography variant="body2" color="text.secondary">
                            <b>{t('labels.inputs')}</b>{' '}
                            {Object.entries(b.inputs).map(([port, bind]) =>
                              `${port} ← ${deviceById.get(bind.deviceId)?.name ?? bind.deviceId}.${bind.capabilityId}`).join(' · ')}
                          </Typography>
                        )}

                        <Typography variant="body2" color="text.secondary">
                          <b>{t('labels.output')}</b>{' '}
                          {vdev && vdev.state && Object.keys(vdev.state).length > 0
                            ? Object.entries(vdev.state).map(([k, v]) => `${k}=${fmt(v)}`).join(' · ')
                            : <em>{t('labels.noSamples')}</em>}
                        </Typography>
                        {Object.keys(b.outputs).length > 0 && (
                          <Typography variant="body2" color="text.secondary">
                            <b>{t('labels.drives')}</b>{' '}
                            {Object.entries(b.outputs).map(([cap, bind]) =>
                              `${cap} → ${deviceById.get(bind.deviceId)?.name ?? bind.deviceId}.${bind.capabilityId}`).join(' · ')}
                          </Typography>
                        )}
                      </Box>
                      <Stack direction="row" spacing={0.5} alignItems="center" sx={{ flexShrink: 0 }}>
                        {isGovernor && stage < 2 && (
                          <Tooltip title={t('promote.tooltip')}>
                            <Button size="small" variant="outlined" startIcon={<PublishRoundedIcon />}
                              onClick={() => proposePromotion(b)}>
                              {t('promote.button')}
                            </Button>
                          </Tooltip>
                        )}
                        <Tooltip title={t('actions.delete')}>
                          <IconButton onClick={() => remove(b)}><DeleteOutlineRoundedIcon /></IconButton>
                        </Tooltip>
                      </Stack>
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
  const { t } = useTranslation('blocks');
  const type = draft ? catalog.get(draft.typeId) : undefined;
  const capsOf = (deviceId: string) => devices.find((d) => d.id === deviceId)?.capabilities ?? [];

  return (
    <Dialog open={draft !== null} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('dialog.title', { type: type?.title ?? t('dialog.blockFallback') })}</DialogTitle>
      <DialogContent>
        {draft && type && (
          <Stack spacing={2.5} mt={1}>
            <Typography variant="caption" color="text.secondary">{type.description}</Typography>
            <TextField label={t('dialog.name')} value={draft.name} autoFocus required fullWidth
              onChange={(e) => onChange({ ...draft, name: e.target.value })} />

            {type.inputs.length > 0 && (
              <Box>
                <Typography variant="overline" color="text.secondary">{t('dialog.inputsSection')}</Typography>
                {type.inputs.map((port) => {
                  const bind = draft.inputs[port.name] ?? { deviceId: '', capabilityId: '' };
                  return (
                    <Stack key={port.name} direction={{ xs: 'column', sm: 'row' }} spacing={1.5} mt={1}>
                      <TextField select label={t('dialog.deviceSuffix', { port: port.name })} value={bind.deviceId} fullWidth
                        helperText={port.description}
                        onChange={(e) => onChange({
                          ...draft,
                          inputs: { ...draft.inputs, [port.name]: { deviceId: e.target.value, capabilityId: '' } },
                        })}>
                        <MenuItem value=""><em>{t('dialog.none')}</em></MenuItem>
                        {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
                      </TextField>
                      <TextField select label={t('dialog.capability')} value={bind.capabilityId} fullWidth disabled={!bind.deviceId}
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

            {type.outputs.length > 0 && (
              <Box>
                <Typography variant="overline" color="text.secondary">{t('dialog.outputsSection')}</Typography>
                {type.outputs.map((out) => {
                  const bind = draft.outputs[out.id] ?? { deviceId: '', capabilityId: '' };
                  return (
                    <Stack key={out.id} direction={{ xs: 'column', sm: 'row' }} spacing={1.5} mt={1}>
                      <TextField select label={t('dialog.outputDeviceSuffix', { output: out.id })} value={bind.deviceId} fullWidth
                        helperText={t('dialog.outputHelper')}
                        onChange={(e) => onChange({
                          ...draft,
                          outputs: { ...draft.outputs, [out.id]: { deviceId: e.target.value, capabilityId: '' } },
                        })}>
                        <MenuItem value=""><em>{t('dialog.none')}</em></MenuItem>
                        {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
                      </TextField>
                      <TextField select label={t('dialog.capability')} value={bind.capabilityId} fullWidth disabled={!bind.deviceId}
                        onChange={(e) => onChange({
                          ...draft,
                          outputs: { ...draft.outputs, [out.id]: { ...bind, capabilityId: e.target.value } },
                        })}>
                        {capsOf(bind.deviceId).filter((c) => c.writable).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
                      </TextField>
                    </Stack>
                  );
                })}
              </Box>
            )}

            {type.params.length > 0 && (
              <Box>
                <Typography variant="overline" color="text.secondary">{t('dialog.parametersSection')}</Typography>
                <Stack spacing={1.5} mt={1}>
                  {type.params.map((p) => (
                    p.name === 'stage' ? (
                      // ML authority stage (Epic 2B): a friendly selector over the numeric 0/1/2 param.
                      <TextField
                        key={p.name} select label={t('dialog.authorityStage')}
                        value={draft.params[p.name] ?? p.default}
                        helperText={p.description}
                        onChange={(e) => onChange({
                          ...draft, params: { ...draft.params, [p.name]: Number(e.target.value) },
                        })}
                      >
                        <MenuItem value={0}>{t('stageOption.shadow')}</MenuItem>
                        <MenuItem value={1}>{t('stageOption.bounded')}</MenuItem>
                        <MenuItem value={2}>{t('stageOption.full')}</MenuItem>
                      </TextField>
                    ) : (
                      <TextField
                        key={p.name} type="number" label={`${p.name}${p.unit ? ` (${p.unit})` : ''}`}
                        value={draft.params[p.name] ?? p.default}
                        helperText={p.description}
                        onChange={(e) => onChange({
                          ...draft, params: { ...draft.params, [p.name]: Number(e.target.value) },
                        })}
                      />
                    )
                  ))}
                </Stack>
              </Box>
            )}

            <Divider />
            <Typography variant="caption" color="text.secondary">
              {t('dialog.outputsSummary', {
                list: type.outputs.map((o) => `${o.id}${o.writable ? ` (${t('dialog.writable')})` : ''}`).join(', '),
              })}
            </Typography>
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('actions.cancel')}</Button>
        <Button variant="contained" onClick={onSave} disabled={!draft?.name.trim()}>{t('actions.create')}</Button>
      </DialogActions>
    </Dialog>
  );
}
