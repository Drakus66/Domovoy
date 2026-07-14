// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import { computeAutoLayout, LayoutNode, LayoutEdge } from './blockGraphLayout';

describe('computeAutoLayout', () => {
  it('returns a top-left position for every node', () => {
    const nodes: LayoutNode[] = [
      { id: 'sensor', width: 170, height: 60 },
      { id: 'block', width: 190, height: 80 },
      { id: 'relay', width: 170, height: 60 },
    ];
    const edges: LayoutEdge[] = [
      { source: 'sensor', target: 'block' },
      { source: 'block', target: 'relay' },
    ];
    const pos = computeAutoLayout(nodes, edges);
    expect(pos.size).toBe(3);
    for (const n of nodes) {
      const p = pos.get(n.id)!;
      expect(Number.isFinite(p.x)).toBe(true);
      expect(Number.isFinite(p.y)).toBe(true);
    }
  });

  it('lays a chain out left-to-right (source ranks before its target)', () => {
    const nodes: LayoutNode[] = [
      { id: 'a', width: 170, height: 60 },
      { id: 'b', width: 190, height: 60 },
      { id: 'c', width: 170, height: 60 },
    ];
    const pos = computeAutoLayout(nodes, [{ source: 'a', target: 'b' }, { source: 'b', target: 'c' }]);
    expect(pos.get('a')!.x).toBeLessThan(pos.get('b')!.x);
    expect(pos.get('b')!.x).toBeLessThan(pos.get('c')!.x);
  });

  it('ignores edges whose endpoints are not both known nodes', () => {
    const nodes: LayoutNode[] = [{ id: 'only', width: 170, height: 60 }];
    // A dangling edge must not throw or invent a phantom node.
    const pos = computeAutoLayout(nodes, [{ source: 'only', target: 'ghost' }]);
    expect(pos.size).toBe(1);
    expect(pos.has('ghost')).toBe(false);
  });

  it('is deterministic for the same input', () => {
    const nodes: LayoutNode[] = [{ id: 'a', width: 170, height: 60 }, { id: 'b', width: 190, height: 60 }];
    const edges: LayoutEdge[] = [{ source: 'a', target: 'b' }];
    expect(computeAutoLayout(nodes, edges)).toEqual(computeAutoLayout(nodes, edges));
  });
});
