// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { ControlBlock, NewBlock, PortBinding } from '../../api/blocks';

/**
 * Pure model for the interactive control-block flow editor (roadmap Epic 1E). All the risky wiring logic —
 * turning a drawn connection into a {@link PortBinding}, deriving edges from existing bindings, removing a
 * binding when an edge is deleted — lives here so it is unit-testable without a DOM. The React component
 * ({@link BlockGraph}) only maps this to reactflow nodes/handles and persists the result.
 *
 * Handle ids encode the port/capability so a drawn connection is fully determined (no capability picker):
 * a block input port is `in:<port>`, a block output capability is `out:<cap>`; a device exposes `in:<cap>`
 * (as an actuator target) and `out:<cap>` (as a sensor source). Data flows source → target.
 */

export const DEVICE_PREFIX = 'dev:';

export const deviceNodeId = (deviceId: string) => `${DEVICE_PREFIX}${deviceId}`;
export const isDeviceNode = (nodeId: string) => nodeId.startsWith(DEVICE_PREFIX);
export const deviceIdOf = (nodeId: string) => nodeId.slice(DEVICE_PREFIX.length);

export const inputHandle = (portOrCap: string) => `in:${portOrCap}`;
export const outputHandle = (capOrPort: string) => `out:${capOrPort}`;
const handleName = (handle: string) => handle.slice(handle.indexOf(':') + 1);

export interface GraphConnection {
  source: string | null;
  sourceHandle?: string | null;
  target: string | null;
  targetHandle?: string | null;
}

export const edgeId = (source: string, sourceHandle: string, target: string, targetHandle: string) =>
  `${source}|${sourceHandle}->${target}|${targetHandle}`;

export interface DerivedEdge {
  id: string;
  source: string;
  sourceHandle: string;
  target: string;
  targetHandle: string;
  label: string;
}

/** Edges for the current bindings: input bindings (source → block port) and output bindings (block cap → device). */
export function deriveEdges(blocks: ControlBlock[]): DerivedEdge[] {
  const blockByVirtualDevice = new Map(blocks.map((b) => [b.deviceId, b.id]));
  const edges: DerivedEdge[] = [];
  for (const b of blocks) {
    for (const [port, bind] of Object.entries(b.inputs)) {
      if (!bind.deviceId || !bind.capabilityId) continue;
      // The source may be another block's virtual device (composition) or a real device.
      const source = blockByVirtualDevice.get(bind.deviceId) ?? deviceNodeId(bind.deviceId);
      const sh = outputHandle(bind.capabilityId);
      const th = inputHandle(port);
      edges.push({ id: edgeId(source, sh, b.id, th), source, sourceHandle: sh, target: b.id, targetHandle: th, label: bind.capabilityId });
    }
    for (const [cap, bind] of Object.entries(b.outputs)) {
      if (!bind.deviceId || !bind.capabilityId) continue;
      const target = deviceNodeId(bind.deviceId);
      const sh = outputHandle(cap);
      const th = inputHandle(bind.capabilityId);
      edges.push({ id: edgeId(b.id, sh, target, th), source: b.id, sourceHandle: sh, target, targetHandle: th, label: cap });
    }
  }
  return edges;
}

/**
 * Apply a drawn connection, returning a new block array with the binding set — or `null` if the wiring is
 * invalid (missing endpoints, wrong handle direction, or device→device).
 */
export function applyConnection(blocks: ControlBlock[], c: GraphConnection): ControlBlock[] | null {
  if (!c.source || !c.target || !c.sourceHandle || !c.targetHandle) return null;
  if (!c.sourceHandle.startsWith('out:') || !c.targetHandle.startsWith('in:')) return null;

  const byId = new Map(blocks.map((b) => [b.id, b]));
  const sourceCap = handleName(c.sourceHandle);
  const targetName = handleName(c.targetHandle);

  if (!isDeviceNode(c.target)) {
    // Target is a block input port; source is a device capability or another block's output.
    const targetBlock = byId.get(c.target);
    if (!targetBlock) return null;
    const sourceDeviceId = isDeviceNode(c.source) ? deviceIdOf(c.source) : byId.get(c.source)?.deviceId;
    if (!sourceDeviceId) return null;
    const binding: PortBinding = { deviceId: sourceDeviceId, capabilityId: sourceCap };
    return blocks.map((b) => (b.id === targetBlock.id ? { ...b, inputs: { ...b.inputs, [targetName]: binding } } : b));
  }

  // Target is a device (actuator): only a block output may drive it.
  if (isDeviceNode(c.source)) return null; // device → device is not a valid block wire
  const sourceBlock = byId.get(c.source);
  if (!sourceBlock) return null;
  const binding: PortBinding = { deviceId: deviceIdOf(c.target), capabilityId: targetName };
  return blocks.map((b) => (b.id === sourceBlock.id ? { ...b, outputs: { ...b.outputs, [sourceCap]: binding } } : b));
}

/** Remove the binding an edge represents (input binding on the target block, or output binding on the source block). */
export function removeEdgeBinding(blocks: ControlBlock[], id: string): ControlBlock[] {
  const arrow = id.indexOf('->');
  if (arrow < 0) return blocks;
  const [source, sourceHandle] = id.slice(0, arrow).split('|');
  const [target, targetHandle] = id.slice(arrow + 2).split('|');

  if (!isDeviceNode(target)) {
    const port = handleName(targetHandle);
    return blocks.map((b) => {
      if (b.id !== target) return b;
      const inputs = { ...b.inputs };
      delete inputs[port];
      return { ...b, inputs };
    });
  }
  const cap = handleName(sourceHandle);
  return blocks.map((b) => {
    if (b.id !== source) return b;
    const outputs = { ...b.outputs };
    delete outputs[cap];
    return { ...b, outputs };
  });
}

/** The persisted payload for a block (Epic 1E: carries the hand-arranged layout so it survives reloads). */
export function toNewBlock(b: ControlBlock): NewBlock {
  return {
    name: b.name,
    typeId: b.typeId,
    enabled: b.enabled,
    params: b.params,
    options: b.options ?? undefined,
    inputs: b.inputs,
    outputs: b.outputs,
    zoneId: b.zoneId ?? undefined,
    layout: b.layout ?? undefined,
  };
}

/** Ids of blocks whose wiring or layout differs from the saved set — what "Save" needs to persist. */
export function dirtyBlockIds(current: ControlBlock[], original: ControlBlock[]): Set<string> {
  const originalById = new Map(original.map((b) => [b.id, b]));
  const key = (b: ControlBlock) => JSON.stringify({ inputs: b.inputs, outputs: b.outputs, layout: b.layout ?? null });
  const dirty = new Set<string>();
  for (const b of current) {
    const o = originalById.get(b.id);
    if (!o || key(o) !== key(b)) dirty.add(b.id);
  }
  return dirty;
}
