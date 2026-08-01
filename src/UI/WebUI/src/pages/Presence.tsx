// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import {
  Container, Box, Typography, Stack, Button, IconButton, LinearProgress, Alert,
  Card, CardContent, Tooltip, Dialog, DialogTitle, DialogContent, DialogActions,
  TextField, Chip, Switch, FormControlLabel, MenuItem, Divider, InputAdornment,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import PeopleAltRoundedIcon from '@mui/icons-material/PeopleAltRounded';
import PersonPinCircleRoundedIcon from '@mui/icons-material/PersonPinCircleRounded';
import HomeRoundedIcon from '@mui/icons-material/HomeRounded';
import BatteryFullRoundedIcon from '@mui/icons-material/BatteryFullRounded';
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded';
import { presenceApi, Resident, ResidentInput, PresenceSettings, PresenceStatus } from '../api/presence';
import { securityApi, User } from '../api/security';

const EMPTY_RESIDENT: ResidentInput = { displayName: '', userId: null, ownTracksId: null, trackingEnabled: true };

export default function Presence() {
  const { t } = useTranslation('presence');

  const [residents, setResidents] = useState<Resident[]>([]);
  const [users, setUsers] = useState<User[]>([]);
  const [status, setStatus] = useState<PresenceStatus | null>(null);
  const [settings, setSettings] = useState<PresenceSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [draft, setDraft] = useState<ResidentInput | null>(null);
  const [editing, setEditing] = useState<Resident | null>(null);

  const fetchAll = useCallback(async () => {
    setError(null);
    try {
      const [r, u, s] = await Promise.all([
        presenceApi.getResidents(),
        securityApi.getUsers().catch(() => [] as User[]),
        presenceApi.getSettings(),
      ]);
      setResidents(r);
      setUsers(u);
      setSettings(s);
    } catch {
      setError(t('errors.load'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { fetchAll(); }, [fetchAll]);

  // Poll the live status (home/away is a live signal, not stored with the roster).
  const loadStatus = useCallback(() => {
    presenceApi.getStatus().then(setStatus).catch(() => undefined);
  }, []);
  useEffect(() => {
    loadStatus();
    const id = setInterval(loadStatus, 15000);
    return () => clearInterval(id);
  }, [loadStatus]);

  const statusOf = useMemo(() => {
    const map = new Map(status?.residents.map((r) => [r.id, r]) ?? []);
    return (id: string) => map.get(id);
  }, [status]);

  const userName = useMemo(() => new Map(users.map((u) => [u.id, u.displayName])), [users]);

  const openCreate = () => { setEditing(null); setDraft({ ...EMPTY_RESIDENT }); };
  const openEdit = (r: Resident) => {
    setEditing(r);
    setDraft({ displayName: r.displayName, userId: r.userId, ownTracksId: r.ownTracksId, trackingEnabled: r.trackingEnabled });
  };
  const close = () => { setDraft(null); setEditing(null); };
  const save = async () => {
    if (!draft || !draft.displayName.trim()) return;
    const body: ResidentInput = {
      displayName: draft.displayName.trim(),
      userId: draft.userId || null,
      ownTracksId: draft.ownTracksId?.trim() || null,
      trackingEnabled: draft.trackingEnabled,
    };
    try {
      if (editing) await presenceApi.updateResident(editing.id, body);
      else await presenceApi.createResident(body);
      close();
      await fetchAll();
      loadStatus();
    } catch { setError(t('errors.save')); }
  };
  const remove = async (r: Resident) => {
    if (!window.confirm(i18n.t('presence:deleteConfirm', { name: r.displayName }))) return;
    try { await presenceApi.deleteResident(r.id); await fetchAll(); loadStatus(); } catch { setError(t('errors.delete')); }
  };

  const sorted = useMemo(
    () => [...residents].sort((a, b) => a.displayName.localeCompare(b.displayName)),
    [residents],
  );

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Box mb={2}>
          <Typography variant="h4" component="h1" fontWeight={700}>{t('title')}</Typography>
          <Typography variant="caption" color="text.secondary">{t('subtitle')}</Typography>
        </Box>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {/* Aggregate summary — the signal that feeds the presence_mode block / rules. */}
        <Card variant="outlined" sx={{ mb: 3 }}>
          <CardContent sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
            <HomeRoundedIcon color={status?.anyoneHome ? 'success' : 'disabled'} />
            <Box flex={1}>
              <Typography fontWeight={700}>
                {status?.anyoneHome ? t('aggregate.someone', { count: status?.homeCount ?? 0 }) : t('aggregate.nobody')}
              </Typography>
              <Typography variant="caption" color="text.secondary">{t('aggregate.hint')}</Typography>
            </Box>
          </CardContent>
        </Card>

        <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}>
          <Typography variant="h6" fontWeight={700}>{t('residents')}</Typography>
          <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={openCreate}>{t('newResident')}</Button>
        </Stack>

        {sorted.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <PeopleAltRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">{t('empty')}</Typography>
          </Box>
        ) : (
          <Stack spacing={1.5} mb={4}>
            {sorted.map((r) => {
              const st = statusOf(r.id);
              const home = st?.home ?? false;
              return (
                <Card key={r.id} variant="outlined">
                  <CardContent sx={{ display: 'flex', alignItems: 'center', gap: 2, py: 1.5, '&:last-child': { pb: 1.5 } }}>
                    <PersonPinCircleRoundedIcon color={r.trackingEnabled ? (home ? 'success' : 'primary') : 'disabled'} />
                    <Box flex={1} minWidth={0}>
                      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                        <Typography fontWeight={700}>{r.displayName}</Typography>
                        {r.trackingEnabled ? (
                          <Chip size="small" color={home ? 'success' : 'default'} variant={home ? 'filled' : 'outlined'}
                            label={home ? t('state.home') : t('state.away')} />
                        ) : (
                          <Chip size="small" label={t('state.untracked')} variant="outlined" />
                        )}
                        {typeof st?.battery === 'number' && (
                          <Chip size="small" variant="outlined"
                            icon={<BatteryFullRoundedIcon sx={{ fontSize: 14 }} />} label={`${st.battery}%`} />
                        )}
                        {r.userId && (
                          <Chip size="small" variant="outlined" label={userName.get(r.userId) ?? t('linkedUser')} />
                        )}
                      </Stack>
                      <Typography variant="body2" color="text.secondary" noWrap>
                        {r.ownTracksId ? t('sourceBound', { id: r.ownTracksId }) : t('noSource')}
                      </Typography>
                    </Box>
                    <Tooltip title={t('actions.edit')}><IconButton onClick={() => openEdit(r)}><EditRoundedIcon /></IconButton></Tooltip>
                    <Tooltip title={t('actions.delete')}><IconButton onClick={() => remove(r)}><DeleteOutlineRoundedIcon /></IconButton></Tooltip>
                  </CardContent>
                </Card>
              );
            })}
          </Stack>
        )}

        {settings && <PresenceSettingsCard settings={settings} onSaved={setSettings} onError={() => setError(t('errors.save'))} />}
      </Box>

      {/* Resident dialog */}
      <Dialog open={draft !== null} onClose={close} fullWidth maxWidth="sm">
        <DialogTitle>{editing ? t('dialog.editTitle') : t('dialog.newTitle')}</DialogTitle>
        <DialogContent>
          {draft && (
            <Stack spacing={2} mt={1}>
              <TextField
                label={t('dialog.displayName')} value={draft.displayName} autoFocus required fullWidth
                onChange={(e) => setDraft({ ...draft, displayName: e.target.value })}
              />
              <TextField
                select label={t('dialog.user')} value={draft.userId ?? ''} fullWidth
                helperText={t('dialog.userHelp')}
                onChange={(e) => setDraft({ ...draft, userId: e.target.value || null })}
              >
                <MenuItem value="">{t('dialog.noUser')}</MenuItem>
                {users.map((u) => <MenuItem key={u.id} value={u.id}>{u.displayName}</MenuItem>)}
              </TextField>
              <TextField
                label={t('dialog.ownTracksId')} value={draft.ownTracksId ?? ''} fullWidth
                helperText={t('dialog.ownTracksIdHelp')}
                onChange={(e) => setDraft({ ...draft, ownTracksId: e.target.value })}
              />
              {draft.ownTracksId?.trim() && <OwnTracksHint ownTracksId={draft.ownTracksId.trim()} />}
              <FormControlLabel
                control={<Switch checked={draft.trackingEnabled} onChange={(e) => setDraft({ ...draft, trackingEnabled: e.target.checked })} />}
                label={t('dialog.trackingEnabled')}
              />
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={close}>{t('actions.cancel')}</Button>
          <Button variant="contained" onClick={save} disabled={!draft?.displayName.trim()}>{t('actions.save')}</Button>
        </DialogActions>
      </Dialog>
    </Container>
  );
}

/** Shows the copy-ready OwnTracks HTTP endpoint for a resident's tracker id. */
function OwnTracksHint({ ownTracksId }: { ownTracksId: string }) {
  const { t } = useTranslation('presence');
  const url = `${window.location.origin}/api/presence/owntracks?user=${encodeURIComponent(ownTracksId)}`;
  return (
    <Alert severity="info" icon={false} sx={{ '& code': { wordBreak: 'break-all' } }}>
      <Typography variant="caption" color="text.secondary" display="block" gutterBottom>{t('dialog.owntracksHint')}</Typography>
      <Stack direction="row" spacing={1} alignItems="center">
        <Typography component="code" variant="body2">{url}</Typography>
        <Tooltip title={t('actions.copy')}>
          <IconButton size="small" onClick={() => navigator.clipboard?.writeText(url)}><ContentCopyRoundedIcon fontSize="small" /></IconButton>
        </Tooltip>
      </Stack>
    </Alert>
  );
}

/** Geofence radius + away-grace + the optional ingest token. */
function PresenceSettingsCard({
  settings, onSaved, onError,
}: {
  settings: PresenceSettings;
  onSaved: (s: PresenceSettings) => void;
  onError: () => void;
}) {
  const { t } = useTranslation('presence');
  const [radius, setRadius] = useState(String(settings.homeRadiusMeters));
  const [grace, setGrace] = useState(String(settings.awayGraceSeconds));
  const [token, setToken] = useState(settings.ownTracksToken ?? '');
  const [saving, setSaving] = useState(false);

  const save = async () => {
    setSaving(true);
    try {
      const saved = await presenceApi.saveSettings({
        homeRadiusMeters: Number(radius) || 150,
        awayGraceSeconds: Number(grace) || 180,
        ownTracksToken: token.trim() || null,
      });
      onSaved(saved);
    } catch { onError(); } finally { setSaving(false); }
  };

  return (
    <Card variant="outlined">
      <CardContent>
        <Typography variant="h6" fontWeight={700} gutterBottom>{t('settings.title')}</Typography>
        <Typography variant="body2" color="text.secondary" mb={2}>{t('settings.hint')}</Typography>
        <Stack spacing={2} maxWidth={420}>
          <TextField
            label={t('settings.radius')} type="number" value={radius}
            InputProps={{ endAdornment: <InputAdornment position="end">{t('settings.meters')}</InputAdornment> }}
            helperText={t('settings.radiusHelp')}
            onChange={(e) => setRadius(e.target.value)}
          />
          <TextField
            label={t('settings.grace')} type="number" value={grace}
            InputProps={{ endAdornment: <InputAdornment position="end">{t('settings.seconds')}</InputAdornment> }}
            helperText={t('settings.graceHelp')}
            onChange={(e) => setGrace(e.target.value)}
          />
          <Divider />
          <TextField
            label={t('settings.token')} value={token} fullWidth
            helperText={t('settings.tokenHelp')}
            onChange={(e) => setToken(e.target.value)}
          />
          <Box>
            <Button variant="contained" onClick={save} disabled={saving}>{t('actions.save')}</Button>
          </Box>
        </Stack>
      </CardContent>
    </Card>
  );
}
