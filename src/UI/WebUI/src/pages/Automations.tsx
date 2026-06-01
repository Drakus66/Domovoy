import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Container, Box, Typography, Stack, Button, IconButton, LinearProgress, Alert,
  Card, CardContent, Tooltip, Chip, Dialog, DialogTitle, DialogContent,
  DialogActions, TextField, MenuItem, FormControlLabel, Checkbox, Divider, Drawer,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import ShieldRoundedIcon from '@mui/icons-material/ShieldRounded';
import BoltRoundedIcon from '@mui/icons-material/BoltRounded';
import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import ScienceRoundedIcon from '@mui/icons-material/ScienceRounded';
import {
  automationsApi, AutomationRule, AutoHistoryEntry, RuleTrigger, RuleCondition, RuleAction, NewRule, RuleStatus,
} from '../api/automations';
import { replayApi, ReplayResult } from '../api/replay';
import { capabilityDevicesApi, CapabilityDevice } from '../api/capabilityDevices';

const OPERATORS = ['eq', 'ne', 'gt', 'lt', 'gte', 'lte', 'changed'];
// User-selectable lifecycle (Proposed/Approved are reserved for ML proposals, Epic 1F/Phase 2).
const STATUS_OPTIONS: RuleStatus[] = ['Active', 'Shadow', 'Disabled'];
const statusColor = (s: RuleStatus): 'success' | 'info' | 'default' =>
  s === 'Active' ? 'success' : s === 'Shadow' ? 'info' : 'default';

const parseValue = (raw: string): unknown => {
  const s = raw.trim();
  if (s === '') return true;
  if (s.toLowerCase() === 'true') return true;
  if (s.toLowerCase() === 'false') return false;
  const n = Number(s);
  return Number.isNaN(n) ? s : n;
};

const fmt = (v: unknown): string => (typeof v === 'boolean' ? (v ? 'on' : 'off') : String(v ?? ''));

interface DraftState {
  name: string;
  trigDevice: string; trigCap: string; trigOp: string; trigValue: string;
  onlyDark: boolean;
  actDevice: string; actCap: string; actValue: string;
  autoOffSeconds: number;
  status: RuleStatus;
}

const EMPTY_DRAFT: DraftState = {
  name: '', trigDevice: '', trigCap: '', trigOp: 'eq', trigValue: 'true',
  onlyDark: false, actDevice: '', actCap: '', actValue: 'true', autoOffSeconds: 0,
  status: 'Active',
};

export default function Automations() {
  const [rules, setRules] = useState<AutomationRule[]>([]);
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [draft, setDraft] = useState<DraftState | null>(null);
  const [historyFor, setHistoryFor] = useState<AutomationRule | 'all' | null>(null);
  const [simulateFor, setSimulateFor] = useState<AutomationRule | null>(null);

  const deviceName = useCallback(
    (id?: string | null) => devices.find((d) => d.id === id)?.name ?? id ?? '—',
    [devices],
  );

  const load = useCallback(async () => {
    setError(null);
    try {
      const [r, d] = await Promise.all([automationsApi.getRules(), capabilityDevicesApi.getDevices()]);
      setRules(r);
      setDevices(d);
    } catch {
      setError('Failed to load automations. Check ApiGateway / DbGateway connection.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const changeStatus = async (rule: AutomationRule, next: RuleStatus) => {
    if (next === rule.status) return;
    setRules((prev) => prev.map((x) => (x.id === rule.id ? { ...x, status: next } : x)));
    try { await automationsApi.setStatus(rule.id, next); }
    catch { setError('Failed to change status'); load(); }
  };

  const remove = async (rule: AutomationRule) => {
    if (!window.confirm(`Delete automation "${rule.name}"?`)) return;
    try { await automationsApi.deleteRule(rule.id); await load(); }
    catch { setError('Failed to delete'); }
  };

  const triggerText = useCallback((t: RuleTrigger): string => {
    if (t.type === 'DeviceState') return `${t.capabilityId ?? 'any'} ${t.operator ?? 'eq'} ${fmt(t.value)} · ${deviceName(t.deviceId)}`;
    if (t.type === 'Time') return `schedule ${t.cron ?? ''}`;
    return `${t.sun ?? 'sun'}${t.offsetMinutes ? ` ${t.offsetMinutes > 0 ? '+' : ''}${t.offsetMinutes}m` : ''}`;
  }, [deviceName]);

  const conditionText = (c: RuleCondition): string => {
    if (c.type === 'Sun') return c.dark === false ? 'while light' : 'while dark';
    if (c.type === 'TimeOfDay') return `${c.fromTime}–${c.toTime}`;
    if (c.type === 'Mode') return `mode ${c.mode}`;
    return `${c.capabilityId} ${c.operator ?? 'eq'} ${fmt(c.value)}`;
  };

  const actionText = useCallback((a: RuleAction): string => {
    if (a.type === 'Command') return `set ${Object.entries(a.set ?? {}).map(([k, v]) => `${k}=${fmt(v)}`).join(', ')} · ${deviceName(a.deviceId)}`;
    if (a.type === 'Delay') return `wait ${a.delaySeconds}s`;
    return `notify "${a.message ?? ''}"`;
  }, [deviceName]);

  const save = async () => {
    if (!draft || !draft.name.trim() || !draft.trigDevice || !draft.trigCap || !draft.actDevice || !draft.actCap) return;

    const triggers: RuleTrigger[] = [{
      type: 'DeviceState', deviceId: draft.trigDevice, capabilityId: draft.trigCap,
      operator: draft.trigOp, value: draft.trigOp === 'changed' ? null : parseValue(draft.trigValue),
    }];
    const conditions: RuleCondition[] = draft.onlyDark ? [{ type: 'Sun', dark: true }] : [];
    const actions: RuleAction[] = [{ type: 'Command', deviceId: draft.actDevice, set: { [draft.actCap]: parseValue(draft.actValue) } }];
    if (draft.autoOffSeconds > 0) {
      actions.push({ type: 'Delay', delaySeconds: draft.autoOffSeconds });
      actions.push({ type: 'Command', deviceId: draft.actDevice, set: { [draft.actCap]: false } });
    }

    const rule: NewRule = { name: draft.name.trim(), description: null, status: draft.status, triggers, conditions, actions };
    try {
      await automationsApi.createRule(rule);
      setDraft(null);
      await load();
    } catch {
      setError('Failed to create rule');
    }
  };

  const sorted = useMemo(
    () => [...rules].sort((a, b) => Number(b.isProtected) - Number(a.isProtected) || a.name.localeCompare(b.name)),
    [rules],
  );

  return (
    <Container maxWidth="lg">
      <Box py={{ xs: 3, md: 4 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={3}>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>Automations</Typography>
            <Typography variant="caption" color="text.secondary">
              Deterministic trigger → condition → action rules. Protected safety-floor rules can't be disabled.
            </Typography>
          </Box>
          <Stack direction="row" spacing={1}>
            <Button startIcon={<HistoryRoundedIcon />} onClick={() => setHistoryFor('all')}>History</Button>
            <Button variant="contained" startIcon={<AddRoundedIcon />} onClick={() => setDraft({ ...EMPTY_DRAFT })}>
              New rule
            </Button>
          </Stack>
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}

        {sorted.length === 0 && !loading ? (
          <Box textAlign="center" py={8}>
            <BoltRoundedIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
            <Typography color="text.secondary">No automations yet. Create one to let the house run itself.</Typography>
          </Box>
        ) : (
          <Stack spacing={1.5}>
            {sorted.map((rule) => (
              <Card key={rule.id} variant="outlined">
                <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
                  <Stack direction="row" alignItems="flex-start" spacing={2}>
                    <Box flex={1} minWidth={0}>
                      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap mb={0.5}>
                        <Typography fontWeight={700}>{rule.name}</Typography>
                        {rule.isProtected && (
                          <Chip size="small" icon={<ShieldRoundedIcon />} label="Protected" color="warning" variant="outlined" />
                        )}
                        <Chip size="small" label={rule.status}
                          color={statusColor(rule.status)} variant="outlined" />
                      </Stack>
                      <Typography variant="body2" color="text.secondary">
                        <b>When</b> {rule.triggers.map(triggerText).join(' or ')}
                      </Typography>
                      {rule.conditions.length > 0 && (
                        <Typography variant="body2" color="text.secondary">
                          <b>If</b> {rule.conditions.map(conditionText).join(' and ')}
                        </Typography>
                      )}
                      <Typography variant="body2" color="text.secondary">
                        <b>Then</b> {rule.actions.map(actionText).join(' → ')}
                      </Typography>
                    </Box>
                    <Stack direction="row" alignItems="center" spacing={0.5}>
                      <Tooltip title="Dry-run over history">
                        <IconButton onClick={() => setSimulateFor(rule)}><ScienceRoundedIcon /></IconButton>
                      </Tooltip>
                      <Tooltip title="Run history">
                        <IconButton onClick={() => setHistoryFor(rule)}><HistoryRoundedIcon /></IconButton>
                      </Tooltip>
                      {rule.isProtected ? (
                        <Chip size="small" label="Always on" variant="outlined" />
                      ) : (
                        <TextField
                          select size="small" value={rule.status} sx={{ minWidth: 110 }}
                          onChange={(e) => changeStatus(rule, e.target.value as RuleStatus)}
                        >
                          {STATUS_OPTIONS.map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
                        </TextField>
                      )}
                      <Tooltip title={rule.isProtected ? 'Protected rules cannot be deleted' : 'Delete'}>
                        <span>
                          <IconButton onClick={() => remove(rule)} disabled={rule.isProtected}>
                            <DeleteOutlineRoundedIcon />
                          </IconButton>
                        </span>
                      </Tooltip>
                    </Stack>
                  </Stack>
                </CardContent>
              </Card>
            ))}
          </Stack>
        )}
      </Box>

      <CreateDialog draft={draft} devices={devices} onChange={setDraft} onClose={() => setDraft(null)} onSave={save} />
      <HistoryDrawer target={historyFor} onClose={() => setHistoryFor(null)} />
      <SimulateDialog rule={simulateFor} onClose={() => setSimulateFor(null)} />
    </Container>
  );
}

function CreateDialog({
  draft, devices, onChange, onClose, onSave,
}: {
  draft: DraftState | null;
  devices: CapabilityDevice[];
  onChange: (d: DraftState) => void;
  onClose: () => void;
  onSave: () => void;
}) {
  const caps = (deviceId: string) => devices.find((d) => d.id === deviceId)?.capabilities ?? [];
  const valid = draft && draft.name.trim() && draft.trigDevice && draft.trigCap && draft.actDevice && draft.actCap;

  return (
    <Dialog open={draft !== null} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>New automation</DialogTitle>
      <DialogContent>
        {draft && (
          <Stack spacing={2.5} mt={1}>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
              <TextField label="Name" value={draft.name} autoFocus required fullWidth
                onChange={(e) => onChange({ ...draft, name: e.target.value })} />
              <TextField select label="Status" value={draft.status} sx={{ minWidth: 140 }}
                helperText={draft.status === 'Shadow' ? 'logs, never acts' : ' '}
                onChange={(e) => onChange({ ...draft, status: e.target.value as RuleStatus })}>
                <MenuItem value="Active">Active</MenuItem>
                <MenuItem value="Shadow">Shadow</MenuItem>
              </TextField>
            </Stack>

            <Box>
              <Typography variant="overline" color="text.secondary">When (trigger)</Typography>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} mt={0.5}>
                <TextField select label="Device" value={draft.trigDevice} fullWidth
                  onChange={(e) => onChange({ ...draft, trigDevice: e.target.value, trigCap: '' })}>
                  {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
                </TextField>
                <TextField select label="Capability" value={draft.trigCap} fullWidth disabled={!draft.trigDevice}
                  onChange={(e) => onChange({ ...draft, trigCap: e.target.value })}>
                  {caps(draft.trigDevice).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
                </TextField>
              </Stack>
              <Stack direction="row" spacing={1.5} mt={1.5}>
                <TextField select label="Operator" value={draft.trigOp} sx={{ width: 140 }}
                  onChange={(e) => onChange({ ...draft, trigOp: e.target.value })}>
                  {OPERATORS.map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}
                </TextField>
                <TextField label="Value" value={draft.trigValue} fullWidth disabled={draft.trigOp === 'changed'}
                  helperText="true / false / number / text"
                  onChange={(e) => onChange({ ...draft, trigValue: e.target.value })} />
              </Stack>
              <FormControlLabel sx={{ mt: 0.5 }}
                control={<Checkbox checked={draft.onlyDark} onChange={(e) => onChange({ ...draft, onlyDark: e.target.checked })} />}
                label="Only when dark (after sunset)" />
            </Box>

            <Divider />

            <Box>
              <Typography variant="overline" color="text.secondary">Then (action)</Typography>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} mt={0.5}>
                <TextField select label="Device" value={draft.actDevice} fullWidth
                  onChange={(e) => onChange({ ...draft, actDevice: e.target.value, actCap: '' })}>
                  {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
                </TextField>
                <TextField select label="Capability" value={draft.actCap} fullWidth disabled={!draft.actDevice}
                  onChange={(e) => onChange({ ...draft, actCap: e.target.value })}>
                  {caps(draft.actDevice).filter((c) => c.writable).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
                </TextField>
              </Stack>
              <Stack direction="row" spacing={1.5} mt={1.5}>
                <TextField label="Value" value={draft.actValue} fullWidth
                  onChange={(e) => onChange({ ...draft, actValue: e.target.value })} />
                <TextField type="number" label="Auto-off after (s)" value={draft.autoOffSeconds} sx={{ width: 180 }}
                  helperText="0 = stay on"
                  onChange={(e) => onChange({ ...draft, autoOffSeconds: Number(e.target.value) || 0 })} />
              </Stack>
            </Box>
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={onSave} disabled={!valid}>Create</Button>
      </DialogActions>
    </Dialog>
  );
}

const DAYS_OPTIONS = [1, 7, 30];

/** Dry-run a rule over history (roadmap Epic 1F) — shows when it would have fired, without acting. */
function SimulateDialog({ rule, onClose }: { rule: AutomationRule | null; onClose: () => void }) {
  const [days, setDays] = useState(7);
  const [result, setResult] = useState<ReplayResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);

  const run = useCallback(async (r: AutomationRule, d: number) => {
    setLoading(true); setError(false); setResult(null);
    try { setResult(await replayApi.run(r, d)); }
    catch { setError(true); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => {
    if (rule) run(rule, days);
    else { setResult(null); setError(false); }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rule]);

  return (
    <Dialog open={rule !== null} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Simulate · {rule?.name}</DialogTitle>
      <DialogContent>
        <Stack direction="row" spacing={1} alignItems="center" mb={2} mt={1}>
          <TextField select size="small" label="Window" value={days} sx={{ width: 160 }}
            onChange={(e) => { const d = Number(e.target.value); setDays(d); if (rule) run(rule, d); }}>
            {DAYS_OPTIONS.map((d) => <MenuItem key={d} value={d}>last {d} day{d > 1 ? 's' : ''}</MenuItem>)}
          </TextField>
          {result && (
            <Typography variant="body2" color="text.secondary">
              {result.fires} would fire · {result.hits.length} trigger match(es) · {result.eventsScanned} events scanned
            </Typography>
          )}
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2 }} />}
        {error && <Alert severity="warning">Simulation failed — is the AutomationService reachable?</Alert>}

        {result?.notes.map((n, i) => (
          <Alert key={i} severity="info" sx={{ mb: 1 }}>{n}</Alert>
        ))}

        {result && !loading && result.hits.length === 0 && !error && (
          <Typography variant="body2" color="text.secondary">
            This rule would not have triggered in the selected window.
          </Typography>
        )}

        <Stack spacing={1} mt={1}>
          {result?.hits.map((h, i) => (
            <Stack key={`${h.timestamp}-${i}`} direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Chip size="small" variant="outlined" color={h.conditionsMet ? 'success' : 'default'}
                label={h.conditionsMet ? 'would fire' : 'trigger only'} />
              <Typography variant="body2" color="text.secondary">{h.triggerSummary}</Typography>
              <Box flex={1} />
              <Typography variant="caption" color="text.secondary">{new Date(h.timestamp).toLocaleString()}</Typography>
            </Stack>
          ))}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Close</Button>
      </DialogActions>
    </Dialog>
  );
}

function HistoryDrawer({ target, onClose }: { target: AutomationRule | 'all' | null; onClose: () => void }) {
  const [rows, setRows] = useState<AutoHistoryEntry[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (target === null) return;
    let cancelled = false;
    setLoading(true);
    automationsApi
      .getHistory(target === 'all' ? undefined : target.id, 100)
      .then((r) => { if (!cancelled) setRows(r); })
      .catch(() => { if (!cancelled) setRows([]); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [target]);

  const title = target === 'all' ? 'All run history' : target?.name ?? '';

  return (
    <Drawer anchor="right" open={target !== null} onClose={onClose}
      PaperProps={{ sx: { width: { xs: '100%', sm: 420 }, maxWidth: '100%' } }}>
      <Box p={2.5}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={2}>
          <Typography variant="h6" fontWeight={700}>{title}</Typography>
          <IconButton onClick={onClose} edge="end"><CloseRoundedIcon /></IconButton>
        </Stack>
        {loading && <LinearProgress sx={{ mb: 2 }} />}
        {!loading && rows.length === 0 && (
          <Typography variant="body2" color="text.secondary">No runs recorded yet.</Typography>
        )}
        <Stack spacing={1.25}>
          {rows.map((e, i) => (
            <Box key={`${e.timestamp}-${i}`}>
              <Stack direction="row" alignItems="center" spacing={1} flexWrap="wrap" useFlexGap>
                <Chip size="small" variant="outlined"
                  color={e.success && e.conditionsMet ? 'success' : e.conditionsMet ? 'error' : 'default'}
                  label={e.conditionsMet ? (e.success ? `ran (${e.actionsExecuted})` : 'failed') : 'skipped'} />
                {target === 'all' && <Typography variant="body2" fontWeight={600}>{e.ruleName}</Typography>}
                <Box flex={1} />
                <Typography variant="caption" color="text.secondary">
                  {new Date(e.timestamp).toLocaleString()}
                </Typography>
              </Stack>
              <Typography variant="caption" color="text.secondary">{e.triggerSummary}{e.detail ? ` — ${e.detail}` : ''}</Typography>
            </Box>
          ))}
        </Stack>
      </Box>
    </Drawer>
  );
}
