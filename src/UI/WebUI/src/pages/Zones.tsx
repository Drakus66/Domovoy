// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import {
  Container, Box, Typography, Stack, Button, IconButton, LinearProgress, Alert,
  Card, CardContent, Tooltip, Dialog, DialogTitle, DialogContent, DialogActions,
  TextField, MenuItem, Chip,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import RoomRoundedIcon from '@mui/icons-material/RoomRounded';
import { zonesApi, Zone, ZoneInput } from '../api/zones';
import { capabilityDevicesApi } from '../api/capabilityDevices';
import { deviceLabel, hasZonePointer, stripZonePointer, withZonePointer } from '../components/devices/deviceNaming';
import { useDeviceRename } from '../components/devices/useDeviceRename';
import type { RenameProposal } from '../components/devices/DeviceRenameDialog';

const KIND_OPTIONS = ['floor', 'room', 'outdoor', 'lawn', 'bed', 'gate'];

const EMPTY: ZoneInput = { name: '', description: '', parentZoneId: '', kind: '', order: 0 };

export default function Zones() {
  // The `devices` namespace is loaded so the shared device-naming helpers (autoName / zone pointer)
  // resolve their labels when proposing device renames after a zone rename (Epic 3G, point 4).
  const { t } = useTranslation(['zones', 'devices']);
  const [zones, setZones] = useState<Zone[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Zone | null>(null);
  const [draft, setDraft] = useState<ZoneInput | null>(null);

  // Renaming a zone offers to update the "… в <Zone>" pointer in its devices' names (Epic 3G, point 4).
  const { dialog: renameDialog, requestRename } = useDeviceRename();

  const proposeZoneRename = useCallback(async (zoneId: string, oldName: string, newName: string) => {
    try {
      const devices = await capabilityDevicesApi.getDevices();
      const proposals = devices
        .filter((d) => d.zoneId === zoneId)
        .map((device): RenameProposal | null => {
          const current = deviceLabel(device);
          if (!hasZonePointer(current, oldName)) return null;
          return { device, current, proposed: withZonePointer(stripZonePointer(current, [oldName]), newName) };
        })
        .filter((p): p is RenameProposal => p !== null);
      if (proposals.length > 0) requestRename(proposals);
    } catch {
      // Non-fatal: the zone was renamed; we just couldn't offer to update device names.
    }
  }, [requestRename]);

  const fetchZones = useCallback(async () => {
    setError(null);
    try {
      setZones(await zonesApi.getZones());
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchZones(); }, [fetchZones]);

  const nameById = useMemo(
    () => new Map(zones.map((z) => [z.id, z.name])),
    [zones],
  );

  const openCreate = () => { setEditing(null); setDraft({ ...EMPTY }); };
  const openEdit = (z: Zone) => {
    setEditing(z);
    setDraft({
      name: z.name, description: z.description ?? '', parentZoneId: z.parentZoneId ?? '',
      kind: z.kind ?? '', order: z.order,
    });
  };
  const close = () => { setDraft(null); setEditing(null); };

  const save = async () => {
    if (!draft || !draft.name.trim()) return;
    const newName = draft.name.trim();
    // Capture a rename before close() clears `editing`, so we can offer to update device pointers.
    const renamed = editing && editing.name.trim() !== newName
      ? { id: editing.id, oldName: editing.name }
      : null;
    try {
      if (editing) await zonesApi.updateZone(editing.id, draft);
      else await zonesApi.createZone(draft);
      close();
      await fetchZones();
      if (renamed) await proposeZoneRename(renamed.id, renamed.oldName, newName);
    } catch {
      setError(t('errors.save'));
    }
  };

  const remove = async (z: Zone) => {
    if (!window.confirm(i18n.t('zones:deleteConfirm', { name: z.name }))) return;
    try {
      await zonesApi.deleteZone(z.id);
      await fetchZones();
    } catch {
      setError(t('errors.delete'));
    }
  };

  // Parent options for the current draft: every zone except the one being edited (no self-parenting).
  const parentOptions = useMemo(
    () => zones.filter((z) => z.id !== editing?.id),
    [zones, editing],
  );

  const sorted = useMemo(
    () => [...zones].sort((a, b) => a.order - b.order || a.name.localeCompare(b.name)),
    [zones],
  );

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={3}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t('subtitle')}
            </Typography>
          </Box>
          <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={openCreate}>
            {t('newZone')}
          </Button>
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {sorted.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <RoomRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">{t('empty')}</Typography>
          </Box>
        ) : (
          <Stack spacing={1.5}>
            {sorted.map((z) => (
              <Card key={z.id} variant="outlined">
                <CardContent sx={{ display: 'flex', alignItems: 'center', gap: 2, py: 1.5, '&:last-child': { pb: 1.5 } }}>
                  <RoomRoundedIcon color="primary" />
                  <Box flex={1} minWidth={0}>
                    <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                      <Typography fontWeight={700}>{z.name}</Typography>
                      {z.kind && <Chip size="small" label={z.kind} variant="outlined" />}
                      {z.parentZoneId && (
                        <Chip size="small" label={t('in', { name: nameById.get(z.parentZoneId) ?? '—' })} variant="outlined" />
                      )}
                    </Stack>
                    {z.description && (
                      <Typography variant="body2" color="text.secondary" noWrap>{z.description}</Typography>
                    )}
                  </Box>
                  <Tooltip title={t('actions.edit')}><IconButton onClick={() => openEdit(z)}><EditRoundedIcon /></IconButton></Tooltip>
                  <Tooltip title={t('actions.delete')}><IconButton onClick={() => remove(z)}><DeleteOutlineRoundedIcon /></IconButton></Tooltip>
                </CardContent>
              </Card>
            ))}
          </Stack>
        )}
      </Box>

      <Dialog open={draft !== null} onClose={close} fullWidth maxWidth="sm">
        <DialogTitle>{editing ? t('dialog.editTitle') : t('dialog.newTitle')}</DialogTitle>
        <DialogContent>
          {draft && (
            <Stack spacing={2} mt={1}>
              <TextField
                label={t('dialog.name')} value={draft.name} autoFocus required fullWidth
                onChange={(e) => setDraft({ ...draft, name: e.target.value })}
              />
              <TextField
                label={t('dialog.description')} value={draft.description ?? ''} fullWidth
                onChange={(e) => setDraft({ ...draft, description: e.target.value })}
              />
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                <TextField
                  select label={t('dialog.parentZone')} value={draft.parentZoneId ?? ''} fullWidth
                  onChange={(e) => setDraft({ ...draft, parentZoneId: e.target.value })}
                >
                  <MenuItem value=""><em>{t('dialog.parentNone')}</em></MenuItem>
                  {parentOptions.map((z) => <MenuItem key={z.id} value={z.id}>{z.name}</MenuItem>)}
                </TextField>
                <TextField
                  select label={t('dialog.kind')} value={draft.kind ?? ''} fullWidth
                  onChange={(e) => setDraft({ ...draft, kind: e.target.value })}
                >
                  <MenuItem value=""><em>{t('dialog.kindNone')}</em></MenuItem>
                  {KIND_OPTIONS.map((k) => <MenuItem key={k} value={k}>{k}</MenuItem>)}
                </TextField>
              </Stack>
              <TextField
                type="number" label={t('dialog.order')} value={draft.order ?? 0} sx={{ maxWidth: 140 }}
                onChange={(e) => setDraft({ ...draft, order: Number(e.target.value) || 0 })}
              />
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={close}>{t('actions.cancel')}</Button>
          <Button variant="contained" onClick={save} disabled={!draft?.name.trim()}>{t('actions.save')}</Button>
        </DialogActions>
      </Dialog>

      {renameDialog}
    </Container>
  );
}
