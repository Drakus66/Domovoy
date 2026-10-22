// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** Kinds of electrical node (matches DbGateway PowerNodeKinds, Epic 3C-D). */
export const POWER_NODE_KINDS = ['supply', 'panel', 'circuit'] as const;
export type PowerNodeKind = (typeof POWER_NODE_KINDS)[number];

/** Phases a node can carry; 'three' spreads its load evenly over L1/L2/L3. */
export const POWER_PHASES = ['l1', 'l2', 'l3', 'three'] as const;
export type PowerPhase = (typeof POWER_PHASES)[number];

/** One node of the home's electrical tree (matches DbGateway PowerNode). */
export interface PowerNode {
  id: string;
  name: string;
  kind: PowerNodeKind | string;
  /** Parent node; null for a supply (tree root). */
  parentId?: string | null;
  /** Phase carried; null ⇒ inherited from the parent. */
  phase?: PowerPhase | string | null;
  /** Breaker rating (A) — the default per-circuit budget. */
  breakerAmps?: number | null;
  /** Nominal voltage (V); null ⇒ the site default (230 V). */
  voltage?: number | null;
  /** Device metering this node — the reference for the balance check. */
  meterDeviceId?: string | null;
  /** For a supply: the power_source tier it represents (grid/battery/solar…). */
  powerSourceKind?: string | null;
  order: number;
}

export type PowerNodeInput = Omit<PowerNode, 'id'>;

export const powerTopologyApi = {
  /** All nodes, flat — the client resolves the tree through parentId. */
  getNodes: (): Promise<PowerNode[]> =>
    apiClient.get<PowerNode[]>('/api/power-topology').then((r) => r.data),

  createNode: (node: PowerNodeInput): Promise<PowerNode> =>
    apiClient.post<PowerNode>('/api/power-topology', node).then((r) => r.data),

  updateNode: (id: string, node: PowerNodeInput): Promise<void> =>
    apiClient.put(`/api/power-topology/${encodeURIComponent(id)}`, node).then(() => undefined),

  /** Delete a node; its children move up to its parent and its devices are detached. */
  deleteNode: (id: string): Promise<void> =>
    apiClient.delete(`/api/power-topology/${encodeURIComponent(id)}`).then(() => undefined),
};

/** Depth of a node in the tree — drives the indentation of the flat editor list. */
export const nodeDepth = (node: PowerNode, byId: Map<string, PowerNode>): number => {
  let depth = 0;
  let current = node;
  while (current.parentId && byId.has(current.parentId) && depth < 32) {
    current = byId.get(current.parentId)!;
    depth += 1;
  }
  return depth;
};

/** Nodes ordered as a depth-first tree walk (roots first, children under their parent). */
export const orderedTree = (nodes: PowerNode[]): PowerNode[] => {
  const byParent = new Map<string, PowerNode[]>();
  for (const n of nodes) {
    const key = n.parentId ?? '';
    byParent.set(key, [...(byParent.get(key) ?? []), n]);
  }
  const sort = (list: PowerNode[]) => [...list].sort((a, b) => a.order - b.order || a.name.localeCompare(b.name));

  const out: PowerNode[] = [];
  const walk = (parentKey: string, guard: number) => {
    if (guard > 32) return;
    for (const node of sort(byParent.get(parentKey) ?? [])) {
      out.push(node);
      walk(node.id, guard + 1);
    }
  };
  walk('', 0);
  // Nodes whose parent no longer exists would otherwise vanish from the editor.
  const seen = new Set(out.map((n) => n.id));
  return [...out, ...nodes.filter((n) => !seen.has(n.id))];
};
