// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import dagre from '@dagrejs/dagre';

/**
 * Auto-layout for the interactive control-block flow editor (roadmap Epic 1E). Kept as a pure function over
 * plain node/edge descriptors so it is deterministic and unit-testable without React Flow or a DOM: the
 * component just feeds in the current node sizes and wiring and applies the returned positions.
 *
 * dagre is the official React Flow layout example; we run it left-to-right (sensor → block → actuator, which
 * matches the data-flow direction the graph already encodes) and translate its node-centre coordinates to the
 * top-left origin React Flow expects.
 */

export interface LayoutNode {
  id: string;
  width: number;
  height: number;
}

export interface LayoutEdge {
  source: string;
  target: string;
}

export interface Point {
  x: number;
  y: number;
}

/** Compute a left-to-right layout, returning top-left positions keyed by node id. */
export function computeAutoLayout(nodes: LayoutNode[], edges: LayoutEdge[]): Map<string, Point> {
  const g = new dagre.graphlib.Graph();
  g.setGraph({ rankdir: 'LR', nodesep: 36, ranksep: 90, marginx: 16, marginy: 16 });
  g.setDefaultEdgeLabel(() => ({}));

  const ids = new Set(nodes.map((n) => n.id));
  for (const n of nodes) g.setNode(n.id, { width: n.width, height: n.height });
  // Only wire edges between known nodes — a dangling endpoint would make dagre invent a phantom node.
  for (const e of edges) {
    if (ids.has(e.source) && ids.has(e.target)) g.setEdge(e.source, e.target);
  }

  dagre.layout(g);

  const out = new Map<string, Point>();
  for (const n of nodes) {
    const laid = g.node(n.id);
    // dagre centres the node; React Flow positions its top-left corner.
    out.set(n.id, { x: Math.round(laid.x - n.width / 2), y: Math.round(laid.y - n.height / 2) });
  }
  return out;
}
