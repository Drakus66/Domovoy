// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useId } from 'react';
import { useTheme } from '@mui/material';

/**
 * Shared visual language for the recharts charts — the non-component half (hooks + prop
 * factories), kept apart from the JSX in chartChrome.tsx so each module satisfies Fast Refresh
 * (react-refresh lint rule). Recharts recognises children by element type, so the kit is prop
 * factories to spread into real recharts elements (a wrapper component would be invisible to the
 * chart). Colors are JS values read from the active theme: CSS `var(--mui-*)` is not valid inside
 * SVG presentation attributes.
 */

export interface ChartPalette {
  /** The subject series (accent). */
  series: string;
  /** A context / "fact" series next to the subject. */
  reference: string;
  /** De-emphasised marks: sparklines on tiles, trend hints. */
  muted: string;
  grid: string;
  cursor: string;
  axisText: string;
  surface: string;
}

export function useChartPalette(): ChartPalette {
  const theme = useTheme();
  return {
    series: theme.palette.primary.main,
    reference: theme.palette.text.secondary,
    muted: theme.palette.text.disabled,
    grid: theme.palette.divider,
    cursor: theme.palette.divider,
    axisText: theme.palette.text.secondary,
    surface: theme.palette.background.paper,
  };
}

/** Unique SVG gradient id per chart instance; useId() contains ':' which breaks `url(#…)`. */
export function useChartGradientId(prefix = 'grad'): string {
  return `${prefix}-${useId().replace(/:/g, '')}`;
}

/** Horizontal-only solid hairline grid — recessive, never dashed. */
export const gridProps = (p: ChartPalette) => ({
  stroke: p.grid,
  strokeWidth: 1,
  vertical: false,
});

export const xAxisProps = (p: ChartPalette) => ({
  tick: { fontSize: 11, fill: p.axisText },
  tickLine: false,
  axisLine: { stroke: p.grid, strokeWidth: 1 },
});

export const yAxisProps = (p: ChartPalette) => ({
  tick: { fontSize: 11, fill: p.axisText },
  tickLine: false,
  axisLine: false,
});

/** Crosshair: the vertical hairline that tracks the pointer (pass as Tooltip `cursor`). */
export const cursorProps = (p: ChartPalette) => ({
  stroke: p.cursor,
  strokeWidth: 1,
});
