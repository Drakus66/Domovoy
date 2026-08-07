// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Container, Box, Typography, Stack, Button, IconButton, LinearProgress, Alert,
  Card, CardContent, Tooltip, Chip, Dialog, DialogTitle, DialogContent, DialogActions,
  TextField, MenuItem, Divider, Paper, Switch, List, ListItemButton, ListItemIcon,
  ListItemText, Checkbox, Snackbar,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded';
import MovieFilterRoundedIcon from '@mui/icons-material/MovieFilterRounded';
import CameraRoundedIcon from '@mui/icons-material/CameraRounded';
import { scenesApi, Scene, SceneTarget, NewScene } from '../api/scenes';
import { capabilityDevicesApi, CapabilityDevice, Capability, isServiceDevice } from '../api/capabilityDevices';
import { confirmAction } from '../store/confirmStore';

/** The scene-builder draft: mirrors the server model but keeps description as a plain string for the field. */
interface SceneDraft {
  id?: string;
  name: string;
  description: string;
  targets: SceneTarget[];
}

const emptyDraft = (): SceneDraft => ({ name: '', description: '', targets: [] });

const draftFromScene = (s: Scene): SceneDraft => ({
  id: s.id,
  name: s.name,
  description: s.description ?? '',
  targets: s.targets.map((t) => ({ deviceId: t.deviceId, set: { ...t.set } })),
});

/** Snapshot a device's current state, keeping only writable capabilities that have a value (Re-Capture). */
const captureTarget = (device: CapabilityDevice): SceneTarget => {
  const set: Record<string, unknown> = {};
  for (const cap of device.capabilities) {
    if (!cap.writable) continue;
    const v = device.state[cap.id];
    if (v !== undefined && v !== null) set[cap.id] = v;
  }
  return { deviceId: device.id, set };
};

/** A scene is saveable when named and it has at least one target carrying at least one value. */
const isDraftValid = (d: SceneDraft): boolean =>
  !!d.name.trim() && d.targets.some((t) => Object.keys(t.set).length > 0);

/** A sensible default value for a freshly added capability, by kind. */
const defaultValue = (cap: Capability): unknown => {
  if (cap.kind === 'Boolean') return true;
  if (cap.kind === 'Enum') return cap.values?.[0] ?? '';
  if (cap.kind === 'Number') return cap.min ?? 0;
  return '';
};

export default function Scenes() {
  const { t } = useTranslation('scenes');
  const [scenes, setScenes] = useState<Scene[]>([]);
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [draft, setDraft] = useState<SceneDraft | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [s, d] = await Promise.all([scenesApi.getScenes(), capabilityDevicesApi.getDevices()]);
      setScenes(s);
      setDevices(d);
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  const deviceName = useCallback(
    (id: string) => devices.find((d) => d.id === id)?.name ?? id,
    [devices],
  );

  const activate = async (scene: Scene) => {
    try {
      await scenesApi.activate(scene.id);
      setToast(t('toast.activated', { name: scene.name }));
    } catch {
      setError(t('errors.activate'));
    }
  };

  const remove = async (scene: Scene) => {
    if (!await confirmAction({ message: t('confirm.delete', { name: scene.name }) })) return;
    try { await scenesApi.deleteScene(scene.id); await load(); }
    catch { setError(t('errors.delete')); }
  };

  const save = async () => {
    if (!draft || !isDraftValid(draft)) return;
    // Drop empty targets (a device that ended up with no values) before persisting.
    const targets = draft.targets.filter((tg) => Object.keys(tg.set).length > 0);
    const payload: NewScene = {
      name: draft.name.trim(),
      description: draft.description.trim() || null,
      icon: null,
      targets,
    };
    try {
      if (draft.id) {
        const existing = scenes.find((s) => s.id === draft.id);
        if (existing) await scenesApi.updateScene(draft.id, { ...existing, ...payload });
      } else {
        await scenesApi.createScene(payload);
      }
      setDraft(null);
      await load();
    } catch {
      setError(draft.id ? t('errors.update') : t('errors.create'));
    }
  };

  const sorted = useMemo(() => [...scenes].sort((a, b) => a.name.localeCompare(b.name)), [scenes]);

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={3}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">{t('subtitle')}</Typography>
          </Box>
          <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={() => setDraft(emptyDraft())}>
            {t('actions.newScene')}
          </Button>
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {sorted.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <MovieFilterRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">{t('empty.none')}</Typography>
          </Box>
        ) : (
          <Stack spacing={1.5}>
            {sorted.map((scene) => (
              <Card key={scene.id} variant="outlined">
                <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                  <Stack direction="row" alignItems="flex-start" spacing={2}>
                    <Box flex={1} minWidth={0}>
                      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.5}>
                        <Typography fontWeight={700}>{scene.name}</Typography>
                        <Chip size="small" variant="outlined"
                          label={t('deviceCount', { count: scene.targets.length })} />
                      </Stack>
                      {scene.description && (
                        <Typography variant="body2" color="text.secondary">{scene.description}</Typography>
                      )}
                      <Typography variant="body2" color="text.secondary">
                        {scene.targets.map((tg) => deviceName(tg.deviceId)).join(', ') || t('empty.noTargets')}
                      </Typography>
                    </Box>
                    <Stack direction="row" alignItems="center" spacing={0.5}>
                      <Button size="small" variant="contained" startIcon={<PlayArrowRoundedIcon />}
                        onClick={() => activate(scene)}>{t('actions.activate')}</Button>
                      <Tooltip title={t('actions.edit')}>
                        <IconButton onClick={() => setDraft(draftFromScene(scene))}><EditRoundedIcon /></IconButton>
                      </Tooltip>
                      <Tooltip title={t('actions.delete')}>
                        <IconButton onClick={() => remove(scene)}><DeleteOutlineRoundedIcon /></IconButton>
                      </Tooltip>
                    </Stack>
                  </Stack>
                </CardContent>
              </Card>
            ))}
          </Stack>
        )}
      </Box>

      <SceneDialog draft={draft} devices={devices} onChange={setDraft} onClose={() => setDraft(null)} onSave={save} />
      <Snackbar open={toast !== null} autoHideDuration={3000} onClose={() => setToast(null)}
        message={toast ?? ''} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }} />
    </Container>
  );
}

/**
 * Scene builder — create or edit a scene. Editing here never touches the house; only the Activate button
 * (on the list) or a rule/dashboard sends commands. Targets are captured from live device state
 * (Re-Capture) and then freely edited, or added device by device.
 */
function SceneDialog({
  draft, devices, onChange, onClose, onSave,
}: {
  draft: SceneDraft | null;
  devices: CapabilityDevice[];
  onChange: (d: SceneDraft) => void;
  onClose: () => void;
  onSave: () => void;
}) {
  const { t } = useTranslation('scenes');
  const [capturing, setCapturing] = useState(false);
  const valid = draft ? isDraftValid(draft) : false;
  const editing = !!draft?.id;

  const deviceById = useMemo(() => new Map(devices.map((d) => [d.id, d])), [devices]);

  // Capture (Re-Capture) merges snapshots of the picked devices into the current targets, replacing any
  // target for a device already present so the newest live state wins.
  const capture = (ids: string[]) => {
    if (!draft) return;
    const captured = ids
      .map((id) => deviceById.get(id))
      .filter((d): d is CapabilityDevice => !!d)
      .map(captureTarget);
    const kept = draft.targets.filter((tg) => !ids.includes(tg.deviceId));
    onChange({ ...draft, targets: [...kept, ...captured] });
    setCapturing(false);
  };

  const setTarget = (index: number, target: SceneTarget) =>
    draft && onChange({ ...draft, targets: draft.targets.map((tg, i) => (i === index ? target : tg)) });

  const removeTarget = (index: number) =>
    draft && onChange({ ...draft, targets: draft.targets.filter((_, i) => i !== index) });

  return (
    <Dialog open={draft !== null} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{editing ? t('dialog.editTitle') : t('dialog.createTitle')}</DialogTitle>
      <DialogContent>
        {draft && (
          <Stack spacing={2.5} mt={1}>
            <TextField label={t('dialog.name')} value={draft.name} autoFocus required fullWidth
              onChange={(e) => onChange({ ...draft, name: e.target.value })} />
            <TextField label={t('dialog.description')} value={draft.description} fullWidth
              onChange={(e) => onChange({ ...draft, description: e.target.value })} />

            <Divider />

            <Box>
              <Stack direction="row" alignItems="center" justifyContent="space-between" mb={1}>
                <Typography variant="overline" color="text.secondary">{t('dialog.targets')}</Typography>
                <Button size="small" startIcon={<CameraRoundedIcon />} onClick={() => setCapturing(true)}>
                  {t('actions.capture')}
                </Button>
              </Stack>
              <Typography variant="caption" color="text.secondary" display="block" sx={{ mb: 1.5 }}>
                {t('dialog.targetsHint')}
              </Typography>

              {draft.targets.length === 0 && (
                <Typography variant="body2" color="text.secondary">{t('dialog.noTargets')}</Typography>
              )}

              <Stack spacing={1.5}>
                {draft.targets.map((target, i) => (
                  <TargetEditor key={`${target.deviceId}-${i}`} device={deviceById.get(target.deviceId)}
                    target={target} onChange={(tg) => setTarget(i, tg)} onDelete={() => removeTarget(i)} />
                ))}
              </Stack>
            </Box>
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('actions.cancel')}</Button>
        <Button variant="contained" onClick={onSave} disabled={!valid}>
          {editing ? t('actions.save') : t('actions.create')}
        </Button>
      </DialogActions>

      <CaptureDialog open={capturing} devices={devices} onClose={() => setCapturing(false)} onConfirm={capture} />
    </Dialog>
  );
}

/** One device target: shows each captured capability with a kind-aware value control + add/remove. */
function TargetEditor({
  device, target, onChange, onDelete,
}: {
  device?: CapabilityDevice;
  target: SceneTarget;
  onChange: (t: SceneTarget) => void;
  onDelete: () => void;
}) {
  const { t } = useTranslation('scenes');
  const writable = useMemo(() => (device?.capabilities ?? []).filter((c) => c.writable), [device]);
  const keys = Object.keys(target.set);
  const remaining = writable.filter((c) => !keys.includes(c.id));

  const setValue = (capId: string, value: unknown) =>
    onChange({ ...target, set: { ...target.set, [capId]: value } });

  const removeCap = (capId: string) => {
    const next = { ...target.set };
    delete next[capId];
    onChange({ ...target, set: next });
  };

  const addCap = (cap: Capability) => setValue(cap.id, defaultValue(cap));

  return (
    <Paper variant="outlined" sx={{ p: 1.5, pr: 5, position: 'relative', borderRadius: 2 }}>
      <IconButton size="small" onClick={onDelete} sx={{ position: 'absolute', top: 6, right: 6 }}>
        <DeleteOutlineRoundedIcon fontSize="small" />
      </IconButton>
      <Typography fontWeight={700} variant="body2" mb={1}>
        {device?.name ?? t('unknownDevice')}
      </Typography>
      <Stack spacing={1}>
        {keys.length === 0 && (
          <Typography variant="caption" color="text.secondary">{t('dialog.noValues')}</Typography>
        )}
        {keys.map((capId) => {
          const cap = writable.find((c) => c.id === capId);
          return (
            <Stack key={capId} direction="row" spacing={1} alignItems="center">
              <Typography variant="body2" sx={{ minWidth: 130 }} noWrap>{capId}</Typography>
              <Box flex={1}>
                <CapValueField cap={cap} value={target.set[capId]} onChange={(v) => setValue(capId, v)} />
              </Box>
              <IconButton size="small" onClick={() => removeCap(capId)}>
                <DeleteOutlineRoundedIcon fontSize="small" />
              </IconButton>
            </Stack>
          );
        })}
      </Stack>
      {remaining.length > 0 && (
        <TextField select size="small" value="" label={t('dialog.addCapability')} sx={{ mt: 1.5, minWidth: 200 }}
          onChange={(e) => { const cap = remaining.find((c) => c.id === e.target.value); if (cap) addCap(cap); }}>
          {remaining.map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
        </TextField>
      )}
    </Paper>
  );
}

/** Kind-aware value control: Switch for Boolean, Select for Enum, otherwise a number/text field. */
function CapValueField({
  cap, value, onChange,
}: {
  cap?: Capability;
  value: unknown;
  onChange: (v: unknown) => void;
}) {
  if (cap?.kind === 'Boolean') {
    return <Switch checked={value === true} onChange={(e) => onChange(e.target.checked)} />;
  }
  if (cap?.kind === 'Enum' && cap.values && cap.values.length > 0) {
    return (
      <TextField select size="small" fullWidth value={String(value ?? '')}
        onChange={(e) => onChange(e.target.value)}>
        {cap.values.map((v) => <MenuItem key={v} value={v}>{v}</MenuItem>)}
      </TextField>
    );
  }
  const isNumber = cap?.kind === 'Number';
  return (
    <TextField size="small" fullWidth type={isNumber ? 'number' : 'text'}
      value={value === null || value === undefined ? '' : String(value)}
      onChange={(e) => onChange(isNumber ? (e.target.value === '' ? '' : Number(e.target.value)) : e.target.value)} />
  );
}

/** Device multi-picker for Re-Capture: pick which devices' current state to snapshot into the scene. */
function CaptureDialog({
  open, devices, onClose, onConfirm,
}: {
  open: boolean;
  devices: CapabilityDevice[];
  onClose: () => void;
  onConfirm: (ids: string[]) => void;
}) {
  const { t } = useTranslation('scenes');
  const [selected, setSelected] = useState<string[]>([]);
  const [search, setSearch] = useState('');

  // Only devices with at least one writable capability can contribute to a scene.
  const candidates = useMemo(
    () => devices
      .filter((d) => !isServiceDevice(d) && d.capabilities.some((c) => c.writable))
      .filter((d) => d.name.toLowerCase().includes(search.toLowerCase()))
      .sort((a, b) => a.name.localeCompare(b.name)),
    [devices, search],
  );

  useEffect(() => { if (open) { setSelected([]); setSearch(''); } }, [open]);

  const toggle = (id: string) =>
    setSelected((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{t('capture.title')}</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" mb={1.5}>{t('capture.subtitle')}</Typography>
        <TextField size="small" fullWidth placeholder={t('capture.search')} value={search}
          onChange={(e) => setSearch(e.target.value)} sx={{ mb: 1 }} />
        <List dense sx={{ maxHeight: 320, overflow: 'auto' }}>
          {candidates.map((d) => (
            <ListItemButton key={d.id} onClick={() => toggle(d.id)} dense>
              <ListItemIcon sx={{ minWidth: 40 }}>
                <Checkbox edge="start" checked={selected.includes(d.id)} tabIndex={-1} disableRipple />
              </ListItemIcon>
              <ListItemText primary={d.name} secondary={d.model || d.adapterSource} />
            </ListItemButton>
          ))}
          {candidates.length === 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ px: 1, py: 2 }}>
              {t('capture.none')}
            </Typography>
          )}
        </List>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('actions.cancel')}</Button>
        <Button variant="contained" disabled={selected.length === 0} onClick={() => onConfirm(selected)}>
          {t('capture.add', { count: selected.length })}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
