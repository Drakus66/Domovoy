// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Breadcrumbs, ButtonBase, Link as MuiLink, Typography, useTheme } from '@mui/material';
import { SunburstChart } from 'recharts';

/**
 * One node of a hierarchy rendered as a sunburst ring segment. `value` is absolute and a parent's
 * value must be ≥ the sum of its children (the un-covered remainder simply shows as empty ring
 * space). A node without `fill` inherits its parent's color.
 */
export interface SunburstNode {
  id: string;
  name: string;
  value: number;
  fill?: string;
  children?: SunburstNode[];
  /** Free-form payload for the hover hint / detail panes of the caller. */
  meta?: Record<string, unknown>;
}

interface HierarchySunburstProps {
  root: SunburstNode;
  /** Square side in px (recharts needs explicit numbers — no ResponsiveContainer here). */
  size?: number;
  valueFormatter: (value: number) => string;
  /** Extra hint line under the value (e.g. "линия · L2"). */
  hintFormatter?: (node: SunburstNode) => string | null;
  onCurrentChange?: (node: SunburstNode) => void;
}

// The recharts payload carries our node so click/hover handlers can reach id/meta.
interface ChartDatum {
  name: string;
  value: number;
  fill?: string;
  children?: ChartDatum[];
  node: SunburstNode;
}

const toChartData = (n: SunburstNode): ChartDatum => ({
  name: n.name,
  value: n.value,
  fill: n.fill,
  children: n.children?.map(toChartData),
  node: n,
});

// The built-in per-arc label prints the raw value on every segment — noise at any density.
// Identity is carried by the center, the hover hint and the breadcrumbs instead.
const HIDDEN_TEXT = { fill: 'transparent', stroke: 'none', fontSize: '0', pointerEvents: 'none' } as const;

/**
 * Drill-down sunburst over recharts' SunburstChart (Bklit-inspired): breadcrumbs on top, the
 * current node's total in the center hole (click = one level up), a hover hint below the chart.
 * Generic over any tree — the power grid today, zones→devices tomorrow.
 */
export default function HierarchySunburst({
  root, size = 320, valueFormatter, hintFormatter, onCurrentChange,
}: HierarchySunburstProps) {
  const theme = useTheme();
  const { t } = useTranslation('common');
  // The drill path is stored as ids and re-resolved on every data refresh: a new fetch produces
  // new node objects, and a stale object path would silently pin the view to old data.
  const [pathIds, setPathIds] = useState<string[]>([]);
  const [hover, setHover] = useState<SunburstNode | null>(null);

  const path = useMemo(() => {
    const out: SunburstNode[] = [root];
    for (const id of pathIds) {
      const next = out[out.length - 1].children?.find((c) => c.id === id);
      if (!next) break; // the node vanished from fresh data — truncate the drill
      out.push(next);
    }
    return out;
  }, [root, pathIds]);
  const current = path[path.length - 1];

  const data = useMemo(() => toChartData(current), [current]);

  const innerRadius = Math.max(48, Math.round(size * 0.17));

  const drillInto = (node: SunburstNode) => {
    if (!node.children?.length) return;
    // The clicked node is somewhere in the current subtree; extend the path by its chain.
    const chain = findChain(current, node.id);
    if (!chain) return;
    setPathIds([...path.slice(1).map((n) => n.id), ...chain]);
    setHover(null);
    onCurrentChange?.(node);
  };

  const jumpTo = (index: number) => {
    setPathIds(path.slice(1, index + 1).map((n) => n.id));
    setHover(null);
    onCurrentChange?.(path[index]);
  };

  const hovered = hover ?? current;
  const hint = hintFormatter?.(hovered) ?? null;

  return (
    <Box sx={{ width: '100%', display: 'flex', flexDirection: 'column', alignItems: 'center', minWidth: 0 }}>
      <Breadcrumbs
        separator="›"
        sx={{ mb: 0.5, maxWidth: '100%', '& .MuiBreadcrumbs-ol': { flexWrap: 'nowrap' } }}
      >
        {path.map((node, i) => (
          i === path.length - 1 ? (
            <Typography key={node.id} variant="caption" fontWeight={700} noWrap>{node.name}</Typography>
          ) : (
            <MuiLink
              key={node.id}
              component="button"
              type="button"
              variant="caption"
              underline="hover"
              color="text.secondary"
              onClick={() => jumpTo(i)}
              sx={{ maxWidth: 120, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
            >
              {node.name}
            </MuiLink>
          )
        ))}
      </Breadcrumbs>

      <Box sx={{ position: 'relative', width: size, height: size, maxWidth: '100%' }}>
        <SunburstChart
          width={size}
          height={size}
          data={data}
          innerRadius={innerRadius}
          // 2px surface-colored gaps between segments; rings breathe the same 2px.
          stroke={theme.palette.background.paper}
          padding={2}
          ringPadding={2}
          fill={theme.palette.primary.main}
          textOptions={HIDDEN_TEXT}
          onClick={(d) => drillInto((d as unknown as ChartDatum).node)}
          onMouseEnter={(d) => setHover((d as unknown as ChartDatum).node)}
          onMouseLeave={() => setHover(null)}
        />
        {/* Center hole: the current node's headline; click climbs one level. */}
        <ButtonBase
          onClick={() => path.length > 1 && jumpTo(path.length - 2)}
          disabled={path.length <= 1}
          aria-label={path.length > 1 ? t('flip.back') : undefined}
          sx={{
            position: 'absolute', top: '50%', left: '50%',
            transform: 'translate(-50%, -50%)',
            width: innerRadius * 2 - 8, height: innerRadius * 2 - 8,
            borderRadius: '50%', flexDirection: 'column', textAlign: 'center', px: 1,
          }}
        >
          <Typography
            variant="caption" color="text.secondary" noWrap
            sx={{ maxWidth: '100%', display: 'block' }}
          >
            {current.name}
          </Typography>
          <Typography variant="body2" fontWeight={700} noWrap sx={{ maxWidth: '100%' }}>
            {valueFormatter(current.value)}
          </Typography>
        </ButtonBase>
      </Box>

      {/* Hover readout: fixed-height so the layout never jumps. Value leads, the label follows. */}
      <Box sx={{ minHeight: 36, mt: 0.5, textAlign: 'center' }}>
        <Typography variant="body2" component="span" fontWeight={700}>
          {valueFormatter(hovered.value)}
        </Typography>
        <Typography variant="body2" component="span" color="text.secondary">
          {` · ${hovered.name}`}
          {current.value > 0 && hovered !== current
            ? ` · ${Math.round((hovered.value / current.value) * 100)}%`
            : ''}
        </Typography>
        {hint && (
          <Typography variant="caption" color="text.secondary" display="block">{hint}</Typography>
        )}
      </Box>
    </Box>
  );
}

/** Ids from `from`'s children down to the node with `id` (excluding `from` itself). */
function findChain(from: SunburstNode, id: string): string[] | null {
  for (const child of from.children ?? []) {
    if (child.id === id) return [child.id];
    const rest = findChain(child, id);
    if (rest) return [child.id, ...rest];
  }
  return null;
}
