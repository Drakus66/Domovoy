// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import type { EnergyBreakdownResult, EnergyNodeBreakdown } from '../../../api/energy';
import type { SunburstNode } from '../../charts/HierarchySunburst';

/**
 * Assembly of the electrical-topology sunburst (pure — unit-tested directly). The topology tree is
 * already embedded in /api/energy/breakdown (nodeId/parentId/kind/phase), so no extra fetch is
 * needed; devices join in through their energyProfile.circuitId, prepared by the caller.
 */

/** One device attached to a circuit, prepared by the caller (names resolved, power read). */
export interface PowerDeviceRow {
  id: string;
  name: string;
  circuitId: string;
  kwh: number;
  watts: number;
}

export type PowerMetric = 'kwh' | 'watts';

export interface PowerSunburstPalette {
  /** Branch hues for phases L1 / L2 / L3; `rest` covers three-phase and unphased branches. */
  l1: string;
  l2: string;
  l3: string;
  rest: string;
  /** Neutral for "unaccounted" / "unmapped" — never a categorical hue. */
  neutral: string;
}

export interface PowerSunburstLabels {
  root: string;
  unaccounted: string;
  unmapped: string;
}

// Depth shading: the branch keeps one hue, deeper rings fade via hex-alpha steps
// (theme palette colors are hex — the `${hue}CC` pattern is already used across the tiles).
const DEPTH_ALPHA = ['', 'CC', '99', '73'];

const shade = (hue: string, depth: number) =>
  `${hue}${DEPTH_ALPHA[Math.min(depth, DEPTH_ALPHA.length - 1)]}`;

const metricOf = (b: EnergyNodeBreakdown | undefined, metric: PowerMetric) =>
  metric === 'kwh' ? (b?.kwh ?? 0) : (b?.powerW ?? 0);

// Below this share of nothing the synthetic segments only add noise.
const EPSILON = 0.001;

export function buildPowerSunburst(
  breakdown: EnergyBreakdownResult,
  devices: PowerDeviceRow[],
  metric: PowerMetric,
  palette: PowerSunburstPalette,
  labels: PowerSunburstLabels,
): SunburstNode | null {
  if (breakdown.nodes.length === 0) return null;

  const byId = new Map(breakdown.nodes.map((n) => [n.nodeId, n]));
  const children = new Map<string, EnergyNodeBreakdown[]>();
  const roots: EnergyNodeBreakdown[] = [];
  for (const n of breakdown.nodes) {
    if (n.parentId && byId.has(n.parentId)) {
      children.set(n.parentId, [...(children.get(n.parentId) ?? []), n]);
    } else {
      roots.push(n);
    }
  }

  const devicesByCircuit = new Map<string, PowerDeviceRow[]>();
  for (const d of devices) {
    devicesByCircuit.set(d.circuitId, [...(devicesByCircuit.get(d.circuitId) ?? []), d]);
  }

  // Phase inherits up the tree (mirrors the server's PhaseOf; idempotent if already resolved).
  const phaseOf = (n: EnergyNodeBreakdown): string | null => {
    let current: EnergyNodeBreakdown | undefined = n;
    for (let guard = 0; current && guard < 32; guard += 1) {
      if (current.phase && current.phase !== '') return current.phase;
      current = current.parentId ? byId.get(current.parentId) : undefined;
    }
    return null;
  };

  const hueForPhase = (phase: string | null): string => {
    if (phase === 'l1') return palette.l1;
    if (phase === 'l2') return palette.l2;
    if (phase === 'l3') return palette.l3;
    return palette.rest;
  };

  const buildNode = (n: EnergyNodeBreakdown, depth: number): SunburstNode => {
    const hue = hueForPhase(phaseOf(n));
    const kids: SunburstNode[] = (children.get(n.nodeId) ?? [])
      .map((c) => buildNode(c, depth + 1));

    for (const d of devicesByCircuit.get(n.nodeId) ?? []) {
      const value = metric === 'kwh' ? d.kwh : d.watts;
      kids.push({
        id: `dev:${d.id}`,
        name: d.name,
        value,
        fill: shade(hue, depth + 1),
        meta: { kind: 'device' },
      });
    }

    // The meter's reading nothing explains — an honest gray slice inside the node's own span.
    const unaccounted = metric === 'kwh'
      ? (n.unaccountedKwh ?? 0)
      : Math.max(0, (n.meterPowerW ?? 0) - n.powerW);
    if (unaccounted > EPSILON) {
      kids.push({
        id: `unaccounted:${n.nodeId}`,
        name: labels.unaccounted,
        value: unaccounted,
        fill: palette.neutral,
        meta: { kind: 'unaccounted' },
      });
    }

    kids.sort((a, b) => b.value - a.value);

    // The breakdown value is the authoritative subtree sum, but the arcs of the children must
    // never overflow the parent's span — rounding drift resolves toward the larger of the two.
    const own = metricOf(n, metric) + (metric === 'kwh' ? (n.unaccountedKwh ?? 0) : unaccounted);
    const value = Math.max(own, kids.reduce((s, k) => s + k.value, 0));

    return {
      id: n.nodeId,
      name: n.name,
      value,
      fill: shade(hue, depth),
      children: kids.length > 0 ? kids : undefined,
      meta: {
        kind: n.kind,
        phase: phaseOf(n),
        limitWatts: n.limitWatts ?? null,
        meterKwh: n.meterKwh ?? null,
        unaccountedKwh: n.unaccountedKwh ?? null,
        deviceCount: n.deviceCount,
      },
    };
  };

  const topBranches = roots.map((r) => buildNode(r, 0));

  const unmapped = metric === 'kwh' ? breakdown.unmappedKwh : breakdown.unmappedPowerW;
  if (unmapped > EPSILON) {
    topBranches.push({
      id: '__unmapped',
      name: labels.unmapped,
      value: unmapped,
      fill: palette.neutral,
      meta: { kind: 'unmapped' },
    });
  }

  return {
    id: '__root',
    name: labels.root,
    value: topBranches.reduce((s, b) => s + b.value, 0),
    children: topBranches,
  };
}
