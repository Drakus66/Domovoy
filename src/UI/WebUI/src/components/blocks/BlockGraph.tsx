// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ReactFlow, Background, Controls, MarkerType, Handle, Position, useNodesState, useEdgesState,
  type Edge, type Node, type Connection, type NodeChange, type NodeProps, type ReactFlowInstance,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { Box, Typography, Button, Stack, Autocomplete, TextField, CircularProgress, Tooltip, IconButton } from '@mui/material';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import UndoRoundedIcon from '@mui/icons-material/UndoRounded';
import RedoRoundedIcon from '@mui/icons-material/RedoRounded';
import AutoFixHighRoundedIcon from '@mui/icons-material/AutoFixHighRounded';
import { ControlBlock, BlockCatalogEntry } from '../../api/blocks';
import { CapabilityDevice } from '../../api/capabilityDevices';
import {
  applyConnection, deriveEdges, removeEdgeBinding, dirtyBlockIds,
  deviceNodeId, isDeviceNode, inputHandle, outputHandle,
} from './blockGraphModel';
import { computeAutoLayout, LayoutNode, LayoutEdge } from './blockGraphLayout';
import { historyReducer, initHistory, canUndo, canRedo, type History, type HistoryAction } from './history';

/**
 * Interactive control-block flow editor (roadmap Epic 1E over Epic 1H). Blocks and the devices they wire to are
 * nodes; each block input port and output capability is a labelled handle, so a drawn connection maps
 * unambiguously to a {@link import('./blockGraphModel').applyConnection PortBinding} (no capability picker).
 * Drag to arrange (positions persist as `layout`), draw a wire to bind a port, double-click a wire to unbind,
 * auto-arrange to lay the graph out with dagre, and undo/redo any wiring or layout edit.
 *
 * All wiring logic is the pure {@link import('./blockGraphModel')} model, layout is the pure
 * {@link import('./blockGraphLayout')} helper, and undo/redo is the pure {@link import('./history')} reducer —
 * this component is the React Flow shell over the three.
 */

// Node geometry is fixed-pixel (Epic 2Q graph redesign): a single-line ellipsized header of a known height
// plus one row per port, so handle positions are computed — a long block name can never push labels onto
// ports or misalign handles (the old percentage-of-measured-height scheme did exactly that).
const BLOCK_WIDTH = 200;
const DEVICE_WIDTH = 180;
const HEADER_H = 40;      // block header: name + type id
const DEV_HEADER_H = 28;  // device header: name only
const ROW_H = 22;
const PAD_BOTTOM = 6;

const blockNodeHeight = (inputs: number, outputs: number) => HEADER_H + (inputs + outputs) * ROW_H + PAD_BOTTOM;
const deviceNodeHeight = (caps: number) => DEV_HEADER_H + caps * ROW_H + PAD_BOTTOM;

type BlockNodeData = { name: string; typeId: string; inputs: string[]; outputs: string[] };
type DeviceNodeData = { label: string; caps: string[] };

const portLabelSx = { fontSize: 10, color: 'text.secondary', lineHeight: 1 } as const;

function BlockNode({ data }: NodeProps<Node<BlockNodeData>>) {
  return (
    <Box sx={{
      position: 'relative', width: BLOCK_WIDTH, pb: `${PAD_BOTTOM}px`,
      bgcolor: 'background.paper', border: '1.5px solid var(--mui-palette-primary-main)',
      borderRadius: 2, boxShadow: 1,
    }}>
      <Box sx={{
        height: HEADER_H, px: 1, pt: 0.5, boxSizing: 'border-box',
        borderBottom: '1px solid var(--mui-palette-divider)',
      }}>
        {/* Single-line + ellipsis: the header height stays fixed no matter how long the user named the block. */}
        <Typography noWrap title={data.name} sx={{ fontSize: 12.5, fontWeight: 700, lineHeight: 1.4 }}>
          {data.name}
        </Typography>
        <Typography noWrap sx={{ fontSize: 9.5, color: 'primary.main', lineHeight: 1.3 }}>{data.typeId}</Typography>
      </Box>
      {/* One row per port: inputs first (labels left), then outputs (labels right) — no side-by-side collisions. */}
      {data.inputs.map((p) => (
        <Box key={`in-${p}`} sx={{ height: ROW_H, display: 'flex', alignItems: 'center', px: 1 }}>
          <Typography noWrap title={p} sx={portLabelSx}>{p}</Typography>
        </Box>
      ))}
      {data.outputs.map((o) => (
        <Box key={`out-${o}`} sx={{ height: ROW_H, display: 'flex', alignItems: 'center', justifyContent: 'flex-end', px: 1 }}>
          <Typography noWrap title={o} sx={{ ...portLabelSx, color: 'text.primary', fontWeight: 600 }}>{o}</Typography>
        </Box>
      ))}
      {data.inputs.map((p, i) => (
        <Handle key={`h-in-${p}`} type="target" position={Position.Left} id={inputHandle(p)}
          style={{ top: HEADER_H + (i + 0.5) * ROW_H, background: 'var(--mui-palette-info-main)' }} />
      ))}
      {data.outputs.map((o, j) => (
        <Handle key={`h-out-${o}`} type="source" position={Position.Right} id={outputHandle(o)}
          style={{ top: HEADER_H + (data.inputs.length + j + 0.5) * ROW_H, background: 'var(--mui-palette-primary-main)' }} />
      ))}
    </Box>
  );
}

function DeviceNode({ data }: NodeProps<Node<DeviceNodeData>>) {
  return (
    <Box sx={{
      position: 'relative', width: DEVICE_WIDTH, pb: `${PAD_BOTTOM}px`,
      bgcolor: 'background.paper', border: '1px dashed var(--mui-palette-info-main)',
      borderRadius: 2,
    }}>
      <Box sx={{
        height: DEV_HEADER_H, px: 1, display: 'flex', alignItems: 'center', boxSizing: 'border-box',
        borderBottom: '1px solid var(--mui-palette-divider)',
      }}>
        <Typography noWrap title={data.label} sx={{ fontSize: 11.5, fontWeight: 600 }}>{data.label}</Typography>
      </Box>
      {data.caps.map((c) => (
        <Box key={c} sx={{ height: ROW_H, display: 'flex', alignItems: 'center', justifyContent: 'center', px: 1.5 }}>
          <Typography noWrap title={c} sx={portLabelSx}>{c}</Typography>
        </Box>
      ))}
      {data.caps.map((c, i) => (
        <span key={`h-${c}`}>
          {/* Device capability: a source (sensor → block input) and a target (block output → actuator). */}
          <Handle type="source" position={Position.Right} id={outputHandle(c)}
            style={{ top: DEV_HEADER_H + (i + 0.5) * ROW_H, background: 'var(--mui-palette-info-main)' }} />
          <Handle type="target" position={Position.Left} id={inputHandle(c)}
            style={{ top: DEV_HEADER_H + (i + 0.5) * ROW_H, background: 'var(--mui-palette-success-main)' }} />
        </span>
      ))}
    </Box>
  );
}

const nodeTypes = { block: BlockNode, device: DeviceNode };

export default function BlockGraph({
  blocks,
  devices,
  catalog,
  onSave,
}: {
  blocks: ControlBlock[];
  devices: CapabilityDevice[];
  catalog: BlockCatalogEntry[];
  onSave: (changed: ControlBlock[]) => Promise<void>;
}) {
  const { t } = useTranslation('blocks');

  // The editable document (block wiring + hand-arranged layout) lives in an undo/redo history; `present` is
  // the current draft. Devices manually added to the canvas are ephemeral view state, not part of history.
  const [hist, dispatch] = useReducer(
    historyReducer as (s: History<ControlBlock[]>, a: HistoryAction<ControlBlock[]>) => History<ControlBlock[]>,
    initHistory(blocks),
  );
  const draft = hist.present;
  const commit = useCallback((next: ControlBlock[]) => dispatch({ type: 'set', next }), []);
  const undo = useCallback(() => dispatch({ type: 'undo' }), []);
  const redo = useCallback(() => dispatch({ type: 'redo' }), []);

  const [extraDeviceIds, setExtraDeviceIds] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const rfRef = useRef<ReactFlowInstance | null>(null);

  // Re-seed (and clear history) whenever the saved set changes — after a save/reload upstream.
  useEffect(() => { dispatch({ type: 'reset', present: blocks }); }, [blocks]);

  const catalogByType = useMemo(() => new Map(catalog.map((c) => [c.typeId, c])), [catalog]);
  const deviceById = useMemo(() => new Map(devices.map((d) => [d.id, d])), [devices]);

  // The graph the model implies. Position comes from the persisted layout (or a default lane); live drag
  // positions are owned by React Flow's own node state below, not recomputed here — so a drag never rebuilds.
  const derived = useMemo(() => {
    const blockByVirtual = new Map(draft.map((b) => [b.deviceId, b.id]));

    const referenced = new Set<string>(extraDeviceIds);
    for (const b of draft) {
      for (const bind of [...Object.values(b.inputs), ...Object.values(b.outputs)]) {
        if (bind.deviceId && !blockByVirtual.has(bind.deviceId)) referenced.add(bind.deviceId);
      }
    }

    const nodes: Node[] = [];
    [...referenced].forEach((id, i) => {
      const dev = deviceById.get(id);
      nodes.push({
        id: deviceNodeId(id), type: 'device',
        position: { x: 0, y: i * 170 },
        data: { label: dev?.name ?? `${id.slice(0, 8)}…`, caps: (dev?.capabilities ?? []).map((c) => c.id) } satisfies DeviceNodeData,
      });
    });
    draft.forEach((b, i) => {
      const entry = catalogByType.get(b.typeId);
      nodes.push({
        id: b.id, type: 'block',
        position: b.layout ? { x: b.layout.x, y: b.layout.y } : { x: 420, y: i * 170 },
        data: {
          name: b.name, typeId: b.typeId,
          inputs: (entry?.inputs ?? []).map((p) => p.name),
          outputs: (entry?.outputs ?? []).map((o) => o.id),
        } satisfies BlockNodeData,
      });
    });

    // No always-on wire labels (Epic 2Q declutter): both ends of a wire land on a labelled port row, so a
    // label box on every edge only repeated what the nodes already say.
    const edges: Edge[] = deriveEdges(draft).map((e) => ({
      id: e.id, source: e.source, sourceHandle: e.sourceHandle, target: e.target, targetHandle: e.targetHandle,
      markerEnd: { type: MarkerType.ArrowClosed },
      style: { stroke: 'var(--mui-palette-text-secondary)', strokeWidth: 1.5 },
    }));
    return { nodes, edges };
  }, [draft, extraDeviceIds, deviceById, catalogByType]);

  // React Flow owns the live node/edge state (drag, selection, measured size). Seed it from `derived` on
  // mount, then re-sync ONLY when the graph's content actually changes — keyed on a signature that
  // deliberately ignores position. Without this the 5s device poll (a fresh array of identical data) and
  // every drag frame would rebuild the canvas, which is what made it flicker and drop nodes mid-drag.
  const [nodes, setNodes, onNodesChangeInternal] = useNodesState(derived.nodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(derived.edges);

  const signature = useMemo(() => JSON.stringify({
    n: derived.nodes.map((n) => [n.id, n.type, n.data]),
    e: derived.edges.map((e) => [e.id, e.source, e.sourceHandle, e.target, e.targetHandle]),
  }), [derived]);

  // Undo/redo and auto-arrange change only positions (which `signature` ignores), so they must force a
  // re-sync explicitly. Bumping `rev` does that without letting the device poll rebuild the canvas.
  const [rev, setRev] = useState(0);
  const forceResync = useCallback(() => setRev((r) => r + 1), []);
  const syncedSig = useRef<string>(signature);
  const syncedRev = useRef<number>(rev);
  useEffect(() => {
    if (syncedSig.current === signature && syncedRev.current === rev) return;
    syncedSig.current = signature;
    syncedRev.current = rev;
    setNodes(derived.nodes);
    setEdges(derived.edges);
  }, [signature, rev, derived, setNodes, setEdges]);

  // Let React Flow apply the change (drag/selection/size); on drag end, commit the block's position to
  // layout as one undoable step. Position lives out of `signature`, so this commit does not re-sync.
  const onNodesChange = useCallback((changes: NodeChange[]) => {
    onNodesChangeInternal(changes);
    const settled = changes.filter(
      (ch): ch is NodeChange & { type: 'position'; id: string; position: { x: number; y: number } } =>
        ch.type === 'position' && ch.dragging === false && !!ch.position && !isDeviceNode(ch.id),
    );
    if (settled.length) {
      commit(draft.map((b) => {
        const s = settled.find((c) => c.id === b.id);
        return s ? { ...b, layout: { x: s.position.x, y: s.position.y } } : b;
      }));
    }
  }, [onNodesChangeInternal, draft, commit]);

  const onConnect = useCallback((c: Connection) => {
    const next = applyConnection(draft, c);
    if (next) commit(next);
  }, [draft, commit]);

  // Double-click a wire to unbind it (safe, discoverable; avoids accidental single-click deletes).
  const onEdgeDoubleClick = useCallback((_: unknown, edge: Edge) => {
    commit(removeEdgeBinding(draft, edge.id));
  }, [draft, commit]);

  // Lay the whole graph out with dagre. Block positions persist into the document (undoable + dirty); device
  // positions are ephemeral, so we push them straight onto React Flow's node state.
  const autoArrange = useCallback(() => {
    const layoutNodes: LayoutNode[] = nodes.map((n) => {
      if (n.type === 'block') {
        const d = n.data as BlockNodeData;
        return { id: n.id, width: BLOCK_WIDTH, height: blockNodeHeight(d.inputs.length, d.outputs.length) };
      }
      return { id: n.id, width: DEVICE_WIDTH, height: deviceNodeHeight((n.data as DeviceNodeData).caps.length) };
    });
    const layoutEdges: LayoutEdge[] = edges.map((e) => ({ source: e.source, target: e.target }));
    const pos = computeAutoLayout(layoutNodes, layoutEdges);

    setNodes((ns) => ns.map((n) => { const p = pos.get(n.id); return p ? { ...n, position: p } : n; }));
    commit(draft.map((b) => { const p = pos.get(b.id); return p ? { ...b, layout: { x: p.x, y: p.y } } : b; }));
    requestAnimationFrame(() => rfRef.current?.fitView({ padding: 0.2, duration: 300 }));
  }, [nodes, edges, draft, setNodes, commit]);

  // Undo/redo restore an earlier document; forcing a re-sync moves the canvas nodes back even for a
  // layout-only step (which `signature` alone would not catch).
  const doUndo = useCallback(() => { undo(); forceResync(); }, [undo, forceResync]);
  const doRedo = useCallback(() => { redo(); forceResync(); }, [redo, forceResync]);

  // Ctrl/Cmd+Z undo, Ctrl/Cmd+Shift+Z or Ctrl+Y redo — ignored while typing in a field.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const el = e.target as HTMLElement | null;
      if (el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable)) return;
      if (!(e.ctrlKey || e.metaKey)) return;
      const key = e.key.toLowerCase();
      if (key === 'z' && !e.shiftKey) { e.preventDefault(); doUndo(); }
      else if (key === 'y' || (key === 'z' && e.shiftKey)) { e.preventDefault(); doRedo(); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [doUndo, doRedo]);

  const dirty = useMemo(() => dirtyBlockIds(draft, blocks), [draft, blocks]);

  const save = useCallback(async () => {
    setSaving(true);
    try {
      await onSave(draft.filter((b) => dirty.has(b.id)));
    } finally {
      setSaving(false);
    }
  }, [draft, dirty, onSave]);

  const addableDevices = useMemo(
    () => devices.filter((d) => !extraDeviceIds.includes(d.id)),
    [devices, extraDeviceIds],
  );

  if (blocks.length === 0) {
    return (
      <Box textAlign="center" py={8}>
        <Typography color="text.secondary">{t('empty')}</Typography>
      </Box>
    );
  }

  return (
    <Box>
      <Stack direction="row" spacing={1.5} alignItems="center" mb={1} flexWrap="wrap" useFlexGap>
        <Typography variant="caption" color="text.secondary" sx={{ flex: 1, minWidth: 180 }}>
          {t('graph.hint', 'Drag to arrange · draw a wire between ports to bind · double-click a wire to unbind')}
        </Typography>
        <Stack direction="row" spacing={0.5}>
          <Tooltip title={t('graph.undo', 'Undo (Ctrl+Z)')}>
            <span>
              <IconButton size="small" onClick={doUndo} disabled={!canUndo(hist)}><UndoRoundedIcon fontSize="small" /></IconButton>
            </span>
          </Tooltip>
          <Tooltip title={t('graph.redo', 'Redo (Ctrl+Shift+Z)')}>
            <span>
              <IconButton size="small" onClick={doRedo} disabled={!canRedo(hist)}><RedoRoundedIcon fontSize="small" /></IconButton>
            </span>
          </Tooltip>
        </Stack>
        <Button size="small" variant="outlined" startIcon={<AutoFixHighRoundedIcon />} onClick={autoArrange}>
          {t('graph.autoArrange', 'Auto-arrange')}
        </Button>
        <Autocomplete
          size="small" sx={{ width: 220 }} options={addableDevices} getOptionLabel={(d) => d.name}
          value={null} blurOnSelect clearOnBlur
          onChange={(_, d) => d && setExtraDeviceIds((prev) => [...prev, d.id])}
          renderInput={(params) => <TextField {...params} label={t('graph.addDevice', 'Add device')} />}
        />
        <Button
          variant="contained" size="small" startIcon={saving ? <CircularProgress size={16} color="inherit" /> : <SaveRoundedIcon />}
          disabled={dirty.size === 0 || saving} onClick={save}
        >
          {t('graph.save', 'Save layout & wiring')}{dirty.size ? ` (${dirty.size})` : ''}
        </Button>
      </Stack>
      <Box sx={{ height: 560, border: '1px solid var(--mui-palette-divider)', borderRadius: 2 }}>
        <ReactFlow
          nodes={nodes} edges={edges} nodeTypes={nodeTypes} onInit={(inst) => { rfRef.current = inst; }}
          onNodesChange={onNodesChange} onEdgesChange={onEdgesChange}
          onConnect={onConnect} onEdgeDoubleClick={onEdgeDoubleClick}
          fitView proOptions={{ hideAttribution: true }}
        >
          <Background />
          <Controls showInteractive={false} />
        </ReactFlow>
      </Box>
    </Box>
  );
}
