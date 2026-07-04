import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import ReactFlow, { Background, Controls, MarkerType, type Edge, type Node, type NodeProps } from 'reactflow';
import 'reactflow/dist/style.css';
import {
  Container, Box, Typography, Stack, Button, TextField, MenuItem, IconButton, Alert,
  LinearProgress, Paper, Divider, Tooltip,
} from '@mui/material';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import {
  automationsApi, AutomationRule, RuleTrigger, RuleCondition, RuleAction, NewRule, RuleStatus,
} from '../api/automations';
import { capabilityDevicesApi, CapabilityDevice } from '../api/capabilityDevices';

// ---- helpers ----------------------------------------------------------------

const parseValue = (raw: string): unknown => {
  const s = raw.trim();
  if (s === '' || s.toLowerCase() === 'true') return true;
  if (s.toLowerCase() === 'false') return false;
  const n = Number(s);
  return Number.isNaN(n) ? s : n;
};
const fmt = (v: unknown): string => (typeof v === 'boolean' ? (v ? 'on' : 'off') : String(v ?? ''));

type Selection =
  | { kind: 'trigger' }
  | { kind: 'condition'; index: number }
  | { kind: 'action'; index: number }
  | null;

interface CardData {
  title: string;
  lines: string[];
  tone: 'trigger' | 'condition' | 'action';
  selected: boolean;
}

const TONE: Record<CardData['tone'], string> = {
  trigger: 'var(--mui-palette-primary-main)',
  condition: 'var(--mui-palette-warning-main)',
  action: 'var(--mui-palette-success-main)',
};

// A single Homey-style flow card. Editing happens in the side panel; the node is a summary.
function CardNode({ data }: NodeProps<CardData>) {
  return (
    <Box
      sx={{
        minWidth: 190, maxWidth: 230, borderRadius: 2, px: 1.5, py: 1,
        bgcolor: 'background.paper', color: 'text.primary',
        border: '2px solid', borderColor: data.selected ? TONE[data.tone] : 'divider',
        boxShadow: data.selected ? 4 : 1, cursor: 'pointer',
      }}
    >
      <Typography variant="overline" sx={{ color: TONE[data.tone], lineHeight: 1.4 }}>{data.title}</Typography>
      {data.lines.map((l, i) => (
        <Typography key={i} variant="body2" sx={{ wordBreak: 'break-word' }}>{l}</Typography>
      ))}
    </Box>
  );
}

const nodeTypes = { card: CardNode };

const OPERATORS = ['eq', 'ne', 'gt', 'lt', 'gte', 'lte', 'changed'];
const STATUS_OPTIONS: RuleStatus[] = ['Active', 'Shadow', 'Disabled'];

// ---- page -------------------------------------------------------------------

export default function Flow() {
  const { t: tr } = useTranslation('flow');
  const [rules, setRules] = useState<AutomationRule[]>([]);
  const [devices, setDevices] = useState<CapabilityDevice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  // Editable rule model.
  const [editingId, setEditingId] = useState<string | null>(null);
  const [name, setName] = useState(tr('newFlow'));
  const [status, setStatus] = useState<RuleStatus>('Active');
  const [trigger, setTrigger] = useState<RuleTrigger>({ type: 'DeviceState', operator: 'eq', value: true });
  const [conditions, setConditions] = useState<RuleCondition[]>([]);
  const [actions, setActions] = useState<RuleAction[]>([{ type: 'Command', set: {} }]);
  const [selection, setSelection] = useState<Selection>({ kind: 'trigger' });

  const load = useCallback(async () => {
    setError(null);
    try {
      const [r, d] = await Promise.all([automationsApi.getRules(), capabilityDevicesApi.getDevices()]);
      setRules(r);
      setDevices(d);
    } catch {
      setError(tr('loadError'));
    } finally {
      setLoading(false);
    }
  }, [tr]);
  useEffect(() => { load(); }, [load]);
  // Live device state for context while authoring (realtime-ish via poll).
  useEffect(() => {
    const t = setInterval(() => capabilityDevicesApi.getDevices().then(setDevices).catch(() => undefined), 5000);
    return () => clearInterval(t);
  }, []);

  const deviceName = useCallback((id?: string | null) => devices.find((x) => x.id === id)?.name ?? id ?? '—', [devices]);
  const liveValue = useCallback((deviceId?: string | null, cap?: string | null) => {
    if (!deviceId || !cap) return undefined;
    return devices.find((x) => x.id === deviceId)?.state?.[cap];
  }, [devices]);

  const loadRule = (rule: AutomationRule) => {
    setEditingId(rule.id);
    setName(rule.name);
    setStatus(rule.status);
    setTrigger(rule.triggers[0] ?? { type: 'DeviceState', operator: 'eq', value: true });
    setConditions(rule.conditions ?? []);
    setActions(rule.actions.length ? rule.actions : [{ type: 'Command', set: {} }]);
    setSelection({ kind: 'trigger' });
  };
  const newRule = () => {
    setEditingId(null);
    setName(tr('newFlow'));
    setStatus('Active');
    setTrigger({ type: 'DeviceState', operator: 'eq', value: true });
    setConditions([]);
    setActions([{ type: 'Command', set: {} }]);
    setSelection({ kind: 'trigger' });
  };

  // ---- graph (derived from the model) ----
  const triggerSummary = (t: RuleTrigger): string[] => {
    if (t.type === 'DeviceState') {
      const lv = liveValue(t.deviceId, t.capabilityId);
      return [
        `${t.capabilityId ?? tr('summary.any')} ${t.operator ?? 'eq'} ${fmt(t.value)}`,
        deviceName(t.deviceId),
        ...(lv !== undefined ? [tr('summary.now', { value: fmt(lv) })] : []),
      ];
    }
    if (t.type === 'Time') return [tr('summary.cron', { value: t.cron ?? '' })];
    return [`${t.sun ?? 'sun'}${t.offsetMinutes ? ` ${t.offsetMinutes > 0 ? '+' : ''}${t.offsetMinutes}m` : ''}`];
  };
  const conditionSummary = (c: RuleCondition): string[] => {
    if (c.type === 'Sun') return [c.dark === false ? tr('summary.whileLight') : tr('summary.whileDark')];
    if (c.type === 'TimeOfDay') return [`${c.fromTime}–${c.toTime}`];
    if (c.type === 'Mode') return [tr('summary.mode', { mode: c.mode })];
    return [`${c.capabilityId ?? '?'} ${c.operator ?? 'eq'} ${fmt(c.value)}`, deviceName(c.deviceId)];
  };
  const actionSummary = (a: RuleAction): string[] => {
    if (a.type === 'Command') return [tr('summary.set', { value: Object.entries(a.set ?? {}).map(([k, v]) => `${k}=${fmt(v)}`).join(', ') || '…' }), deviceName(a.deviceId)];
    if (a.type === 'Delay') return [tr('summary.wait', { seconds: a.delaySeconds ?? 0 })];
    return [tr('summary.notify', { message: a.message ?? '' })];
  };

  const nodes: Node<CardData>[] = useMemo(() => {
    const list: Node<CardData>[] = [];
    list.push({
      id: 'trigger', type: 'card', position: { x: 0, y: 140 },
      data: { title: tr('card.when'), lines: triggerSummary(trigger), tone: 'trigger', selected: selection?.kind === 'trigger' },
    });
    conditions.forEach((c, i) => list.push({
      id: `cond-${i}`, type: 'card', position: { x: 320, y: i * 130 },
      data: { title: tr('card.andIf'), lines: conditionSummary(c), tone: 'condition', selected: selection?.kind === 'condition' && selection.index === i },
    }));
    actions.forEach((a, i) => list.push({
      id: `act-${i}`, type: 'card', position: { x: 640, y: i * 130 },
      data: { title: tr('card.then', { index: i + 1 }), lines: actionSummary(a), tone: 'action', selected: selection?.kind === 'action' && selection.index === i },
    }));
    return list;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [trigger, conditions, actions, selection, devices]);

  const edges: Edge[] = useMemo(() => {
    const e: Edge[] = [];
    const arrow = { markerEnd: { type: MarkerType.ArrowClosed }, animated: true };
    if (conditions.length === 0) {
      actions.forEach((_, i) => e.push({ id: `t-a${i}`, source: 'trigger', target: `act-${i}`, ...arrow }));
    } else {
      conditions.forEach((_, i) => e.push({ id: `t-c${i}`, source: 'trigger', target: `cond-${i}`, ...arrow }));
      conditions.forEach((_, ci) => actions.forEach((_, ai) => e.push({ id: `c${ci}-a${ai}`, source: `cond-${ci}`, target: `act-${ai}`, ...arrow })));
    }
    return e;
  }, [conditions, actions]);

  const onNodeClick = (_: unknown, node: Node) => {
    if (node.id === 'trigger') setSelection({ kind: 'trigger' });
    else if (node.id.startsWith('cond-')) setSelection({ kind: 'condition', index: Number(node.id.slice(5)) });
    else if (node.id.startsWith('act-')) setSelection({ kind: 'action', index: Number(node.id.slice(4)) });
  };

  // ---- save ----
  const save = async () => {
    setError(null); setInfo(null);
    if (!name.trim()) { setError(tr('nameRequired')); return; }
    const payload: NewRule = {
      name: name.trim(), description: null, status,
      triggers: [trigger], conditions, actions,
    };
    try {
      if (editingId) {
        const existing = rules.find((r) => r.id === editingId)!;
        await automationsApi.updateRule(editingId, { ...existing, ...payload });
        setInfo(tr('updated'));
      } else {
        const created = await automationsApi.createRule(payload);
        setEditingId(created.id);
        setInfo(tr('created'));
      }
      await load();
    } catch {
      setError(tr('saveError'));
    }
  };

  return (
    <Container maxWidth="xl">
      <Box py={{ xs: 2, md: 3 }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={2} flexWrap="wrap" useFlexGap>
          <Box>
            <Typography variant="h4" component="h1" fontWeight={700}>{tr('title')}</Typography>
            <Typography variant="caption" color="text.secondary">
              {tr('subtitle')}
            </Typography>
          </Box>
          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
            <TextField select size="small" label={tr('open')} value={editingId ?? ''} sx={{ minWidth: 180 }}
              onChange={(e) => { const r = rules.find((x) => x.id === e.target.value); if (r) loadRule(r); }}>
              <MenuItem value=""><em>{tr('selectFlow')}</em></MenuItem>
              {rules.filter((r) => !r.isProtected).map((r) => <MenuItem key={r.id} value={r.id}>{r.name}</MenuItem>)}
            </TextField>
            <Button onClick={newRule}>{tr('new')}</Button>
            <Button variant="contained" startIcon={<SaveRoundedIcon />} onClick={save}>{tr('save')}</Button>
          </Stack>
        </Stack>

        {loading && <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />}
        {error && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setError(null)}>{error}</Alert>}
        {info && <Alert severity="success" sx={{ mb: 2 }} onClose={() => setInfo(null)}>{info}</Alert>}

        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
          <Paper variant="outlined" sx={{ flex: 1, height: 560, borderRadius: 2, overflow: 'hidden' }}>
            <ReactFlow nodes={nodes} edges={edges} nodeTypes={nodeTypes} onNodeClick={onNodeClick}
              fitView proOptions={{ hideAttribution: true }}>
              <Background />
              <Controls showInteractive={false} />
            </ReactFlow>
          </Paper>

          <Paper variant="outlined" sx={{ width: { xs: '100%', md: 340 }, p: 2, borderRadius: 2 }}>
            <SidePanel
              name={name} setName={setName} status={status} setStatus={setStatus}
              selection={selection} devices={devices}
              trigger={trigger} setTrigger={setTrigger}
              conditions={conditions} setConditions={setConditions}
              actions={actions} setActions={setActions}
              setSelection={setSelection}
            />
          </Paper>
        </Stack>
      </Box>
    </Container>
  );
}

// ---- side editor panel ------------------------------------------------------

function SidePanel(props: {
  name: string; setName: (v: string) => void;
  status: RuleStatus; setStatus: (v: RuleStatus) => void;
  selection: Selection; devices: CapabilityDevice[];
  trigger: RuleTrigger; setTrigger: (t: RuleTrigger) => void;
  conditions: RuleCondition[]; setConditions: (c: RuleCondition[]) => void;
  actions: RuleAction[]; setActions: (a: RuleAction[]) => void;
  setSelection: (s: Selection) => void;
}) {
  const { t: tr } = useTranslation('flow');
  const {
    name, setName, status, setStatus, selection, devices,
    trigger, setTrigger, conditions, setConditions, actions, setActions, setSelection,
  } = props;
  const caps = (id?: string | null) => devices.find((d) => d.id === id)?.capabilities ?? [];

  const addCondition = () => { setConditions([...conditions, { type: 'Mode', mode: 'Home' }]); setSelection({ kind: 'condition', index: conditions.length }); };
  const addAction = () => { setActions([...actions, { type: 'Command', set: {} }]); setSelection({ kind: 'action', index: actions.length }); };
  const delCondition = (i: number) => { setConditions(conditions.filter((_, x) => x !== i)); setSelection({ kind: 'trigger' }); };
  const delAction = (i: number) => { setActions(actions.filter((_, x) => x !== i)); setSelection({ kind: 'trigger' }); };

  return (
    <Stack spacing={2}>
      <TextField label={tr('panel.flowName')} size="small" value={name} onChange={(e) => setName(e.target.value)} fullWidth />
      <TextField select label={tr('panel.status')} size="small" value={status} onChange={(e) => setStatus(e.target.value as RuleStatus)} fullWidth>
        {STATUS_OPTIONS.map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
      </TextField>
      <Stack direction="row" spacing={1}>
        <Button size="small" startIcon={<AddRoundedIcon />} onClick={addCondition}>{tr('panel.condition')}</Button>
        <Button size="small" startIcon={<AddRoundedIcon />} onClick={addAction}>{tr('panel.action')}</Button>
      </Stack>
      <Divider />

      {selection?.kind === 'trigger' && (
        <TriggerEditor devices={devices} caps={caps} value={trigger} onChange={setTrigger} />
      )}
      {selection?.kind === 'condition' && conditions[selection.index] && (
        <Stack spacing={2}>
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Typography variant="subtitle2">{tr('panel.conditionN', { index: selection.index + 1 })}</Typography>
            <Tooltip title={tr('panel.delete')}><IconButton size="small" onClick={() => delCondition(selection.index)}><DeleteOutlineRoundedIcon fontSize="small" /></IconButton></Tooltip>
          </Stack>
          <ConditionEditor devices={devices} caps={caps} value={conditions[selection.index]}
            onChange={(c) => setConditions(conditions.map((x, i) => (i === selection.index ? c : x)))} />
        </Stack>
      )}
      {selection?.kind === 'action' && actions[selection.index] && (
        <Stack spacing={2}>
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Typography variant="subtitle2">{tr('panel.actionN', { index: selection.index + 1 })}</Typography>
            <Tooltip title={tr('panel.delete')}><IconButton size="small" onClick={() => delAction(selection.index)}><DeleteOutlineRoundedIcon fontSize="small" /></IconButton></Tooltip>
          </Stack>
          <ActionEditor devices={devices} caps={caps} value={actions[selection.index]}
            onChange={(a) => setActions(actions.map((x, i) => (i === selection.index ? a : x)))} />
        </Stack>
      )}
      {!selection && <Typography variant="body2" color="text.secondary">{tr('panel.selectCard')}</Typography>}
    </Stack>
  );
}

function TriggerEditor({ devices, caps, value, onChange }: {
  devices: CapabilityDevice[]; caps: (id?: string | null) => CapabilityDevice['capabilities'];
  value: RuleTrigger; onChange: (t: RuleTrigger) => void;
}) {
  const { t: tr } = useTranslation('flow');
  return (
    <Stack spacing={2}>
      <Typography variant="subtitle2">{tr('trigger.heading')}</Typography>
      <TextField select label={tr('trigger.type')} size="small" value={value.type}
        onChange={(e) => onChange({ ...value, type: e.target.value as RuleTrigger['type'] })}>
        {['DeviceState', 'Time', 'Sun'].map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}
      </TextField>
      {value.type === 'DeviceState' && (
        <>
          <TextField select label={tr('trigger.device')} size="small" value={value.deviceId ?? ''}
            onChange={(e) => onChange({ ...value, deviceId: e.target.value, capabilityId: '' })}>
            {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
          </TextField>
          <TextField select label={tr('trigger.capability')} size="small" value={value.capabilityId ?? ''} disabled={!value.deviceId}
            onChange={(e) => onChange({ ...value, capabilityId: e.target.value })}>
            {caps(value.deviceId).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
          </TextField>
          <Stack direction="row" spacing={1}>
            <TextField select label={tr('trigger.op')} size="small" value={value.operator ?? 'eq'} sx={{ width: 110 }}
              onChange={(e) => onChange({ ...value, operator: e.target.value })}>
              {OPERATORS.map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}
            </TextField>
            <TextField label={tr('trigger.value')} size="small" fullWidth disabled={value.operator === 'changed'}
              value={fmt(value.value)} onChange={(e) => onChange({ ...value, value: parseValue(e.target.value) })} />
          </Stack>
        </>
      )}
      {value.type === 'Time' && (
        <TextField label={tr('trigger.cron')} size="small" value={value.cron ?? ''}
          onChange={(e) => onChange({ ...value, cron: e.target.value })} />
      )}
      {value.type === 'Sun' && (
        <Stack direction="row" spacing={1}>
          <TextField select label={tr('trigger.event')} size="small" value={value.sun ?? 'Sunset'} fullWidth
            onChange={(e) => onChange({ ...value, sun: e.target.value as RuleTrigger['sun'] })}>
            {['Sunrise', 'Sunset'].map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
          </TextField>
          <TextField type="number" label={tr('trigger.offsetMin')} size="small" value={value.offsetMinutes ?? 0} sx={{ width: 120 }}
            onChange={(e) => onChange({ ...value, offsetMinutes: Number(e.target.value) || 0 })} />
        </Stack>
      )}
    </Stack>
  );
}

function ConditionEditor({ devices, caps, value, onChange }: {
  devices: CapabilityDevice[]; caps: (id?: string | null) => CapabilityDevice['capabilities'];
  value: RuleCondition; onChange: (c: RuleCondition) => void;
}) {
  const { t: tr } = useTranslation('flow');
  return (
    <Stack spacing={2}>
      <TextField select label={tr('condition.type')} size="small" value={value.type}
        onChange={(e) => onChange({ ...value, type: e.target.value as RuleCondition['type'] })}>
        {['DeviceState', 'TimeOfDay', 'Sun', 'Mode'].map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}
      </TextField>
      {value.type === 'DeviceState' && (
        <>
          <TextField select label={tr('condition.device')} size="small" value={value.deviceId ?? ''}
            onChange={(e) => onChange({ ...value, deviceId: e.target.value, capabilityId: '' })}>
            {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
          </TextField>
          <TextField select label={tr('condition.capability')} size="small" value={value.capabilityId ?? ''} disabled={!value.deviceId}
            onChange={(e) => onChange({ ...value, capabilityId: e.target.value })}>
            {caps(value.deviceId).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
          </TextField>
          <Stack direction="row" spacing={1}>
            <TextField select label={tr('condition.op')} size="small" value={value.operator ?? 'eq'} sx={{ width: 110 }}
              onChange={(e) => onChange({ ...value, operator: e.target.value })}>
              {OPERATORS.filter((o) => o !== 'changed').map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}
            </TextField>
            <TextField label={tr('condition.value')} size="small" fullWidth value={fmt(value.value)}
              onChange={(e) => onChange({ ...value, value: parseValue(e.target.value) })} />
          </Stack>
        </>
      )}
      {value.type === 'TimeOfDay' && (
        <Stack direction="row" spacing={1}>
          <TextField label={tr('condition.fromTime')} size="small" value={value.fromTime ?? ''} onChange={(e) => onChange({ ...value, fromTime: e.target.value })} />
          <TextField label={tr('condition.toTime')} size="small" value={value.toTime ?? ''} onChange={(e) => onChange({ ...value, toTime: e.target.value })} />
        </Stack>
      )}
      {value.type === 'Sun' && (
        <TextField select label={tr('condition.daylight')} size="small" value={value.dark === false ? 'light' : 'dark'}
          onChange={(e) => onChange({ ...value, dark: e.target.value === 'dark' })}>
          <MenuItem value="dark">{tr('condition.whileDark')}</MenuItem>
          <MenuItem value="light">{tr('condition.whileLight')}</MenuItem>
        </TextField>
      )}
      {value.type === 'Mode' && (
        <TextField select label={tr('condition.mode')} size="small" value={value.mode ?? 'Home'}
          onChange={(e) => onChange({ ...value, mode: e.target.value })}>
          {['Home', 'Away', 'Night', 'Vacation'].map((m) => <MenuItem key={m} value={m}>{m}</MenuItem>)}
        </TextField>
      )}
    </Stack>
  );
}

function ActionEditor({ devices, caps, value, onChange }: {
  devices: CapabilityDevice[]; caps: (id?: string | null) => CapabilityDevice['capabilities'];
  value: RuleAction; onChange: (a: RuleAction) => void;
}) {
  const { t: tr } = useTranslation('flow');
  const setKey = Object.keys(value.set ?? {})[0] ?? '';
  const setVal = setKey ? (value.set as Record<string, unknown>)[setKey] : '';
  return (
    <Stack spacing={2}>
      <TextField select label={tr('action.type')} size="small" value={value.type}
        onChange={(e) => onChange({ ...value, type: e.target.value as RuleAction['type'] })}>
        {['Command', 'Delay', 'Notify'].map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}
      </TextField>
      {value.type === 'Command' && (
        <>
          <TextField select label={tr('action.device')} size="small" value={value.deviceId ?? ''}
            onChange={(e) => onChange({ ...value, deviceId: e.target.value, set: {} })}>
            {devices.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
          </TextField>
          <Stack direction="row" spacing={1}>
            <TextField select label={tr('action.capability')} size="small" value={setKey} disabled={!value.deviceId} sx={{ flex: 1 }}
              onChange={(e) => onChange({ ...value, set: { [e.target.value]: setVal === '' ? true : setVal } })}>
              {caps(value.deviceId).filter((c) => c.writable).map((c) => <MenuItem key={c.id} value={c.id}>{c.id}</MenuItem>)}
            </TextField>
            <TextField label={tr('action.value')} size="small" sx={{ width: 110 }} disabled={!setKey} value={fmt(setVal)}
              onChange={(e) => onChange({ ...value, set: { [setKey]: parseValue(e.target.value) } })} />
          </Stack>
        </>
      )}
      {value.type === 'Delay' && (
        <TextField type="number" label={tr('action.seconds')} size="small" value={value.delaySeconds ?? 0}
          onChange={(e) => onChange({ ...value, delaySeconds: Number(e.target.value) || 0 })} />
      )}
      {value.type === 'Notify' && (
        <TextField label={tr('action.message')} size="small" value={value.message ?? ''}
          onChange={(e) => onChange({ ...value, message: e.target.value })} />
      )}
    </Stack>
  );
}
