// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import { ControlBlock } from '../../api/blocks';
import {
  applyConnection, deriveEdges, removeEdgeBinding, toNewBlock, dirtyBlockIds,
  deviceNodeId, inputHandle, outputHandle, edgeId,
} from './blockGraphModel';

const block = (over: Partial<ControlBlock>): ControlBlock => ({
  id: 'b1', name: 'B1', typeId: 'thermostat', deviceId: 'vdev-b1',
  enabled: true, params: {}, inputs: {}, outputs: {},
  createdAt: '', updatedAt: '', ...over,
});

describe('blockGraphModel.applyConnection', () => {
  it('binds a device capability to a block input port', () => {
    const b = block({ id: 'b1' });
    const out = applyConnection([b], {
      source: deviceNodeId('sensor-1'), sourceHandle: outputHandle('temperature'),
      target: 'b1', targetHandle: inputHandle('temperature'),
    });
    expect(out).not.toBeNull();
    expect(out![0].inputs.temperature).toEqual({ deviceId: 'sensor-1', capabilityId: 'temperature' });
  });

  it('binds one block output (its virtual device) to another block input — composition', () => {
    const a = block({ id: 'a', deviceId: 'vdev-a' });
    const b = block({ id: 'b' });
    const out = applyConnection([a, b], {
      source: 'a', sourceHandle: outputHandle('value'),
      target: 'b', targetHandle: inputHandle('temperature'),
    });
    expect(out![1].inputs.temperature).toEqual({ deviceId: 'vdev-a', capabilityId: 'value' });
  });

  it('binds a block output capability to a device actuator', () => {
    const b = block({ id: 'b1' });
    const out = applyConnection([b], {
      source: 'b1', sourceHandle: outputHandle('on_off'),
      target: deviceNodeId('relay-9'), targetHandle: inputHandle('on_off'),
    });
    expect(out![0].outputs.on_off).toEqual({ deviceId: 'relay-9', capabilityId: 'on_off' });
  });

  it('rejects invalid wiring (device→device, or wrong handle direction)', () => {
    const b = block({ id: 'b1' });
    expect(applyConnection([b], {
      source: deviceNodeId('d1'), sourceHandle: outputHandle('x'),
      target: deviceNodeId('d2'), targetHandle: inputHandle('y'),
    })).toBeNull();
    expect(applyConnection([b], {
      source: 'b1', sourceHandle: inputHandle('temperature'), // source must be an output handle
      target: deviceNodeId('d2'), targetHandle: inputHandle('y'),
    })).toBeNull();
  });
});

describe('blockGraphModel.deriveEdges + removeEdgeBinding', () => {
  it('derives an edge for an input binding and removes it by edge id', () => {
    const b = block({ id: 'b1', inputs: { temperature: { deviceId: 'sensor-1', capabilityId: 'temperature' } } });
    const [edge] = deriveEdges([b]);
    expect(edge.id).toBe(edgeId(deviceNodeId('sensor-1'), outputHandle('temperature'), 'b1', inputHandle('temperature')));

    const cleared = removeEdgeBinding([b], edge.id);
    expect(cleared[0].inputs.temperature).toBeUndefined();
  });

  it('removes an output binding by its edge id', () => {
    const b = block({ id: 'b1', outputs: { on_off: { deviceId: 'relay-9', capabilityId: 'on_off' } } });
    const [edge] = deriveEdges([b]);
    const cleared = removeEdgeBinding([b], edge.id);
    expect(cleared[0].outputs.on_off).toBeUndefined();
  });
});

describe('blockGraphModel.toNewBlock + dirtyBlockIds', () => {
  it('carries the layout into the persisted payload', () => {
    const b = block({ id: 'b1', layout: { x: 120, y: 40 } });
    expect(toNewBlock(b).layout).toEqual({ x: 120, y: 40 });
  });

  it('marks a block dirty when its bindings or layout change', () => {
    const original = block({ id: 'b1' });
    const moved = block({ id: 'b1', layout: { x: 10, y: 20 } });
    expect(dirtyBlockIds([moved], [original])).toEqual(new Set(['b1']));
    expect(dirtyBlockIds([original], [original])).toEqual(new Set());
  });
});
