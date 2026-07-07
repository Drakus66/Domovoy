// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import ReactFlow, {
  Background, Controls, MarkerType, Handle, Position,
  type Edge, type Node, type Connection, type NodeChange, type NodeProps,
} from 'reactflow';
import 'reactflow/dist/style.css';
import { Box, Typography, Button, Stack, Autocomplete, TextField, CircularProgress } from '@mui/material';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import { ControlBlock, BlockCatalogEntry } from '../../api/blocks';
import { CapabilityDevice } from '../../api/capabilityDevices';
import {
  applyConnection, deriveEdges, removeEdgeBinding, dirtyBlockIds,
  deviceNodeId, isDeviceNode, inputHandle, outputHandle,
} from './blockGraphModel';

/**
 * Interactive control-block flow editor (roadmap Epic 1E over Epic 1H). Blocks and the devices they wire to are
 * nodes; each block input port and output capability is a labelled reactflow handle, so a drawn connection maps
 * unambiguously to a {@link import('./blockGraphModel').applyConnection PortBinding} (no capability picker).
 * Drag to arrange (positions persist as `layout`), draw a wire to bind a port, double-click a wire to unbind.
 * All wiring logic is the pure {@link import('./blockGraphModel')} model; this component is the reactflow shell.
 */

const slot = (i: number, n: number) => `${((i + 1) / (n + 1)) * 100}%`;
const nodeMinHeight = (rows: number) => 34 + Math.max(rows, 1) * 24;

interface BlockNodeData { name: string; typeId: string; inputs: string[]; outputs: string[] }
interface DeviceNodeData { label: string; caps: string[] }

function BlockNode({ data }: NodeProps<BlockNodeData>) {
  return (
    <Box sx={{
      position: 'relative', width: 190, minHeight: nodeMinHeight(Math.max(data.inputs.length, data.outputs.length)),
      background: 'var(--mui-palette-primary-main)', color: '#fff', borderRadius: 2, px: 1, py: 0.75, fontSize: 12,
    }}>
      <Box sx={{ textAlign: 'center', fontWeight: 700, mb: 0.5 }}>{data.name}</Box>
      <Box sx={{ textAlign: 'center', fontSize: 10, opacity: 0.8 }}>{data.typeId}</Box>
      {data.inputs.map((p, i) => (
        <span key={`in-${p}`}>
          <Handle type="target" position={Position.Left} id={inputHandle(p)} style={{ top: slot(i, data.inputs.length), background: '#fff' }} />
          <Box sx={{ position: 'absolute', left: 8, top: slot(i, data.inputs.length), transform: 'translateY(-50%)', fontSize: 9 }}>{p}</Box>
        </span>
      ))}
      {data.outputs.map((o, i) => (
        <span key={`out-${o}`}>
          <Handle type="source" position={Position.Right} id={outputHandle(o)} style={{ top: slot(i, data.outputs.length), background: '#fff' }} />
          <Box sx={{ position: 'absolute', right: 8, top: slot(i, data.outputs.length), transform: 'translateY(-50%)', fontSize: 9 }}>{o}</Box>
        </span>
      ))}
    </Box>
  );
}

function DeviceNode({ data }: NodeProps<DeviceNodeData>) {
  const n = data.caps.length;
  return (
    <Box sx={{
      position: 'relative', width: 170, minHeight: nodeMinHeight(n),
      background: 'var(--mui-palette-background-paper)', border: '1px solid var(--mui-palette-info-main)',
      borderRadius: 2, px: 1, py: 0.75, fontSize: 12,
    }}>
      <Box sx={{ textAlign: 'center', fontWeight: 600, mb: 0.5 }}>{data.label}</Box>
      {data.caps.map((c, i) => (
        <span key={c}>
          {/* Device capability: a source (sensor → block input) and a target (block output → actuator). */}
          <Handle type="source" position={Position.Right} id={outputHandle(c)} style={{ top: slot(i, n), background: 'var(--mui-palette-info-main)' }} />
          <Handle type="target" position={Position.Left} id={inputHandle(c)} style={{ top: slot(i, n), background: 'var(--mui-palette-success-main)' }} />
          <Box sx={{ position: 'absolute', width: '100%', left: 0, top: slot(i, n), transform: 'translateY(-50%)', textAlign: 'center', fontSize: 9, color: 'text.secondary' }}>{c}</Box>
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
  const [draft, setDraft] = useState<ControlBlock[]>(blocks);
  const [positions, setPositions] = useState<Record<string, { x: number; y: number }>>({});
  const [extraDeviceIds, setExtraDeviceIds] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  // Re-seed local edits whenever the saved set changes (after a save/reload upstream).
  useEffect(() => { setDraft(blocks); }, [blocks]);

  const catalogByType = useMemo(() => new Map(catalog.map((c) => [c.typeId, c])), [catalog]);
  const deviceById = useMemo(() => new Map(devices.map((d) => [d.id, d])), [devices]);

  const { nodes, edges } = useMemo(() => {
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
        position: positions[deviceNodeId(id)] ?? { x: 0, y: i * 170 },
        data: { label: dev?.name ?? `${id.slice(0, 8)}…`, caps: (dev?.capabilities ?? []).map((c) => c.id) } as DeviceNodeData,
      });
    });
    draft.forEach((b, i) => {
      const entry = catalogByType.get(b.typeId);
      nodes.push({
        id: b.id, type: 'block',
        position: positions[b.id] ?? (b.layout ? { x: b.layout.x, y: b.layout.y } : { x: 420, y: i * 170 }),
        data: {
          name: b.name, typeId: b.typeId,
          inputs: (entry?.inputs ?? []).map((p) => p.name),
          outputs: (entry?.outputs ?? []).map((o) => o.id),
        } as BlockNodeData,
      });
    });

    const edges: Edge[] = deriveEdges(draft).map((e) => ({
      id: e.id, source: e.source, sourceHandle: e.sourceHandle, target: e.target, targetHandle: e.targetHandle,
      label: e.label, markerEnd: { type: MarkerType.ArrowClosed },
      style: { stroke: 'var(--mui-palette-text-secondary)' }, labelStyle: { fontSize: 10 },
    }));
    return { nodes, edges };
  }, [draft, positions, extraDeviceIds, deviceById, catalogByType]);

  // Track live drag positions; on drag end, commit a block node's position into its persisted layout.
  const onNodesChange = useCallback((changes: NodeChange[]) => {
    setPositions((prev) => {
      const next = { ...prev };
      for (const ch of changes) if (ch.type === 'position' && ch.position) next[ch.id] = ch.position;
      return next;
    });
    const settled = changes.filter(
      (ch): ch is NodeChange & { type: 'position'; id: string; position: { x: number; y: number } } =>
        ch.type === 'position' && ch.dragging === false && !!ch.position && !isDeviceNode(ch.id),
    );
    if (settled.length) {
      setDraft((prev) => prev.map((b) => {
        const s = settled.find((c) => c.id === b.id);
        return s ? { ...b, layout: { x: s.position.x, y: s.position.y } } : b;
      }));
    }
  }, []);

  const onConnect = useCallback((c: Connection) => {
    setDraft((prev) => applyConnection(prev, c) ?? prev);
  }, []);

  // Double-click a wire to unbind it (safe, discoverable; avoids accidental single-click deletes).
  const onEdgeDoubleClick = useCallback((_: unknown, edge: Edge) => {
    setDraft((prev) => removeEdgeBinding(prev, edge.id));
  }, []);

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
          nodes={nodes} edges={edges} nodeTypes={nodeTypes}
          onNodesChange={onNodesChange} onConnect={onConnect} onEdgeDoubleClick={onEdgeDoubleClick}
          fitView proOptions={{ hideAttribution: true }}
        >
          <Background />
          <Controls showInteractive={false} />
        </ReactFlow>
      </Box>
    </Box>
  );
}
