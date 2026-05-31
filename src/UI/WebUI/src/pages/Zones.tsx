import { useCallback, useEffect, useMemo, useState } from 'react';
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

const KIND_OPTIONS = ['floor', 'room', 'outdoor', 'lawn', 'bed', 'gate'];

const EMPTY: ZoneInput = { name: '', description: '', parentZoneId: '', kind: '', order: 0 };

export default function Zones() {
  const [zones, setZones] = useState<Zone[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Zone | null>(null);
  const [draft, setDraft] = useState<ZoneInput | null>(null);

  const fetchZones = useCallback(async () => {
    setError(null);
    try {
      setZones(await zonesApi.getZones());
    } catch {
      setError('Failed to load zones. Check ApiGateway / DbGateway connection.');
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
    try {
      if (editing) await zonesApi.updateZone(editing.id, draft);
      else await zonesApi.createZone(draft);
      close();
      await fetchZones();
    } catch {
      setError('Failed to save zone.');
    }
  };

  const remove = async (z: Zone) => {
    if (!window.confirm(`Delete zone "${z.name}"? Devices in it become Unassigned; child zones move up.`)) return;
    try {
      await zonesApi.deleteZone(z.id);
      await fetchZones();
    } catch {
      setError('Failed to delete zone.');
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
            <Typography variant="h4" component="h1" fontWeight={700}>Zones</Typography>
            <Typography variant="caption" color="text.secondary">
              Areas of the home and grounds — group devices, drive climate-by-zone and automations.
            </Typography>
          </Box>
          <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={openCreate}>
            New zone
          </Button>
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {sorted.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <RoomRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">No zones yet. Create one to start grouping devices.</Typography>
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
                        <Chip size="small" label={`in ${nameById.get(z.parentZoneId) ?? '—'}`} variant="outlined" />
                      )}
                    </Stack>
                    {z.description && (
                      <Typography variant="body2" color="text.secondary" noWrap>{z.description}</Typography>
                    )}
                  </Box>
                  <Tooltip title="Edit"><IconButton onClick={() => openEdit(z)}><EditRoundedIcon /></IconButton></Tooltip>
                  <Tooltip title="Delete"><IconButton onClick={() => remove(z)}><DeleteOutlineRoundedIcon /></IconButton></Tooltip>
                </CardContent>
              </Card>
            ))}
          </Stack>
        )}
      </Box>

      <Dialog open={draft !== null} onClose={close} fullWidth maxWidth="sm">
        <DialogTitle>{editing ? 'Edit zone' : 'New zone'}</DialogTitle>
        <DialogContent>
          {draft && (
            <Stack spacing={2} mt={1}>
              <TextField
                label="Name" value={draft.name} autoFocus required fullWidth
                onChange={(e) => setDraft({ ...draft, name: e.target.value })}
              />
              <TextField
                label="Description" value={draft.description ?? ''} fullWidth
                onChange={(e) => setDraft({ ...draft, description: e.target.value })}
              />
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                <TextField
                  select label="Parent zone" value={draft.parentZoneId ?? ''} fullWidth
                  onChange={(e) => setDraft({ ...draft, parentZoneId: e.target.value })}
                >
                  <MenuItem value=""><em>None (top-level)</em></MenuItem>
                  {parentOptions.map((z) => <MenuItem key={z.id} value={z.id}>{z.name}</MenuItem>)}
                </TextField>
                <TextField
                  select label="Kind" value={draft.kind ?? ''} fullWidth
                  onChange={(e) => setDraft({ ...draft, kind: e.target.value })}
                >
                  <MenuItem value=""><em>None</em></MenuItem>
                  {KIND_OPTIONS.map((k) => <MenuItem key={k} value={k}>{k}</MenuItem>)}
                </TextField>
              </Stack>
              <TextField
                type="number" label="Order" value={draft.order ?? 0} sx={{ maxWidth: 140 }}
                onChange={(e) => setDraft({ ...draft, order: Number(e.target.value) || 0 })}
              />
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={close}>Cancel</Button>
          <Button variant="contained" onClick={save} disabled={!draft?.name.trim()}>Save</Button>
        </DialogActions>
      </Dialog>
    </Container>
  );
}
