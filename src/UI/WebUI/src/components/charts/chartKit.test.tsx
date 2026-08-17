// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import { render, screen } from '../../test/utils';
import { fmtDateTime } from '../../i18n/format';
import { useChartGradientId, gridProps, type ChartPalette } from './chartKit';
import { ChartCrosshairTooltip } from './chartChrome';

const palette: ChartPalette = {
  series: '#4361EE', reference: '#888888', muted: '#666666',
  grid: '#333333', cursor: '#333333', axisText: '#888888', surface: '#0b0f1a',
};

function GradientProbe() {
  const id = useChartGradientId();
  return <div data-testid="gid">{id}</div>;
}

describe('chartKit', () => {
  it('issues a unique, url(#…)-safe gradient id per instance', () => {
    render(
      <>
        <GradientProbe />
        <GradientProbe />
      </>,
    );
    const ids = screen.getAllByTestId('gid').map((el) => el.textContent);
    // Two charts of the same capability on one page must not share <defs> — the old
    // `grad-${capabilityId}` scheme did exactly that.
    expect(ids[0]).not.toBe(ids[1]);
    ids.forEach((id) => {
      expect(id).toBeTruthy();
      expect(id).not.toContain(':');
    });
  });

  it('keeps the grid a solid horizontal hairline (no dashes)', () => {
    const props = gridProps(palette);
    expect(props.vertical).toBe(false);
    expect(props.strokeWidth).toBe(1);
    expect(props).not.toHaveProperty('strokeDasharray');
  });

  it('renders every series at the hovered X with the value leading', () => {
    const ts = Date.UTC(2026, 6, 14, 12, 0, 0);
    render(
      <ChartCrosshairTooltip
        active
        unit="°C"
        label={ts}
        payload={[
          { value: 21.5, name: 'среднее', dataKey: 'avg', stroke: '#123456' },
          { value: 20, name: 'факт', dataKey: 'actual', color: '#654321' },
        ]}
      />,
    );
    expect(screen.getByText('21.5°C')).toBeInTheDocument();
    expect(screen.getByText('20°C')).toBeInTheDocument();
    expect(screen.getByText('среднее')).toBeInTheDocument();
    expect(screen.getByText('факт')).toBeInTheDocument();
    expect(screen.getByText(fmtDateTime(new Date(ts).toISOString()))).toBeInTheDocument();
  });

  it('renders nothing while inactive', () => {
    const { container } = render(
      <ChartCrosshairTooltip active={false} payload={[{ value: 1 }]} label={0} />,
    );
    expect(container).toBeEmptyDOMElement();
  });
});
