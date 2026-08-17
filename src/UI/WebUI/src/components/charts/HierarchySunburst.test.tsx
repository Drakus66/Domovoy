// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '../../test/utils';
import HierarchySunburst, { type SunburstNode } from './HierarchySunburst';

const root: SunburstNode = {
  id: 'root',
  name: 'Дом',
  value: 100,
  children: [
    {
      id: 'a',
      name: 'Alpha',
      value: 60,
      fill: '#123456',
      children: [
        { id: 'a1', name: 'A-one', value: 40 },
        { id: 'a2', name: 'A-two', value: 20 },
      ],
    },
    { id: 'b', name: 'Beta', value: 40, fill: '#654321' },
  ],
};

const fmt = (v: number) => `${v} у.е.`;

const sectorPath = (container: HTMLElement, name: string) =>
  container.querySelector(`g[aria-label="${name}"] path`);

describe('HierarchySunburst', () => {
  // The current total appears twice by design (the center hole + the readout line),
  // so presence is asserted with getAllByText.
  it('renders the total in the center and every branch as a sector', () => {
    const { container } = render(<HierarchySunburst root={root} size={300} valueFormatter={fmt} />);

    expect(screen.getAllByText('100 у.е.').length).toBeGreaterThan(0);
    expect(sectorPath(container, 'Alpha')).toBeTruthy();
    expect(sectorPath(container, 'Beta')).toBeTruthy();
    expect(sectorPath(container, 'A-one')).toBeTruthy();
  });

  it('drills into a branch on click and climbs back via the breadcrumb', () => {
    const { container } = render(<HierarchySunburst root={root} size={300} valueFormatter={fmt} />);

    fireEvent.click(sectorPath(container, 'Alpha')!);

    // The center is re-rooted to the branch; Beta's sector is gone.
    expect(screen.getAllByText('60 у.е.').length).toBeGreaterThan(0);
    expect(sectorPath(container, 'Beta')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Дом' }));
    expect(screen.getAllByText('100 у.е.').length).toBeGreaterThan(0);
    expect(sectorPath(container, 'Beta')).toBeTruthy();
  });

  it('ignores clicks on leaves', () => {
    const { container } = render(<HierarchySunburst root={root} size={300} valueFormatter={fmt} />);

    fireEvent.click(sectorPath(container, 'Beta')!);

    expect(screen.getAllByText('100 у.е.').length).toBeGreaterThan(0);
  });

  it('shows the hovered segment in the readout with its share', () => {
    const { container } = render(<HierarchySunburst root={root} size={300} valueFormatter={fmt} />);

    fireEvent.mouseEnter(sectorPath(container, 'Beta')!);

    expect(screen.getByText(/· Beta · 40%/)).toBeInTheDocument();

    fireEvent.mouseLeave(sectorPath(container, 'Beta')!);
    expect(screen.queryByText(/· Beta/)).not.toBeInTheDocument();
  });
});
