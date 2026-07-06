import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import ReactFlow, { Background, Controls, MarkerType, type Edge, type Node } from 'reactflow';
import 'reactflow/dist/style.css';
import { Box, Typography } from '@mui/material';
import { ControlBlock } from '../../api/blocks';
import { CapabilityDevice } from '../../api/capabilityDevices';

/**
 * Visual graph of the control-block network (roadmap Epic 1E over Epic 1H). Each block is a node; a block's
 * input bindings are edges from their source device (which may be ANOTHER block's virtual device — that is the
 * composition), and its output bindings are edges to the actuator it drives. Read-only view; authoring stays on
 * the typed form (composition is expressed by binding ports, so the graph emerges from the bindings).
 */
export default function BlockGraph({
  blocks,
  devices,
}: {
  blocks: ControlBlock[];
  devices: CapabilityDevice[];
}) {
  const { t } = useTranslation('blocks');
  const deviceName = useMemo(
    () => Object.fromEntries(devices.map((d) => [d.id, d.name])),
    [devices],
  );

  const { nodes, edges } = useMemo(() => {
    // A block projects a virtual capability-device; map that id back to the block so block→block edges connect.
    const blockByVirtualDevice = new Map(blocks.map((b) => [b.deviceId, b.id]));

    const externalInputs = new Set<string>();
    const externalOutputs = new Set<string>();
    for (const b of blocks) {
      for (const bind of Object.values(b.inputs)) {
        if (!blockByVirtualDevice.has(bind.deviceId)) externalInputs.add(bind.deviceId);
      }
      for (const bind of Object.values(b.outputs)) {
        if (!blockByVirtualDevice.has(bind.deviceId)) externalOutputs.add(bind.deviceId);
      }
    }

    const nodes: Node[] = [];
    const deviceNode = (id: string, x: number, y: number, tone: string): Node => ({
      id: `dev:${id}`,
      position: { x, y },
      data: { label: deviceName[id] ?? `${id.slice(0, 8)}…` },
      style: {
        background: 'var(--mui-palette-background-paper)', border: `1px solid ${tone}`,
        borderRadius: 8, padding: 8, fontSize: 12, width: 160,
      },
      sourcePosition: 'right' as never,
      targetPosition: 'left' as never,
    });

    [...externalInputs].forEach((id, i) => nodes.push(deviceNode(id, 0, i * 110, 'var(--mui-palette-info-main)')));
    blocks.forEach((b, i) =>
      nodes.push({
        id: b.id,
        position: { x: 360, y: i * 130 },
        data: { label: `${b.name}\n(${b.typeId})` },
        style: {
          background: 'var(--mui-palette-primary-main)', color: '#fff',
          borderRadius: 8, padding: 10, fontSize: 12, width: 180, whiteSpace: 'pre-line', textAlign: 'center',
        },
        sourcePosition: 'right' as never,
        targetPosition: 'left' as never,
      }),
    );
    [...externalOutputs].forEach((id, i) => nodes.push(deviceNode(id, 720, i * 110, 'var(--mui-palette-success-main)')));

    const edges: Edge[] = [];
    const edge = (from: string, to: string, label: string, i: number): Edge => ({
      id: `${from}->${to}:${label}:${i}`,
      source: from, target: to, label,
      markerEnd: { type: MarkerType.ArrowClosed },
      style: { stroke: 'var(--mui-palette-text-secondary)' },
      labelStyle: { fontSize: 10 },
    });

    let k = 0;
    for (const b of blocks) {
      for (const bind of Object.values(b.inputs)) {
        const src = blockByVirtualDevice.get(bind.deviceId) ?? `dev:${bind.deviceId}`;
        edges.push(edge(src, b.id, bind.capabilityId, k++));
      }
      for (const bind of Object.values(b.outputs)) {
        const tgt = blockByVirtualDevice.get(bind.deviceId) ?? `dev:${bind.deviceId}`;
        edges.push(edge(b.id, tgt, bind.capabilityId, k++));
      }
    }

    return { nodes, edges };
  }, [blocks, deviceName]);

  if (blocks.length === 0) {
    return (
      <Box textAlign="center" py={8}>
        <Typography color="text.secondary">{t('empty')}</Typography>
      </Box>
    );
  }

  return (
    <Box sx={{ height: 560, border: '1px solid var(--mui-palette-divider)', borderRadius: 2 }}>
      <ReactFlow nodes={nodes} edges={edges} fitView proOptions={{ hideAttribution: true }}>
        <Background />
        <Controls showInteractive={false} />
      </ReactFlow>
    </Box>
  );
}
