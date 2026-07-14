// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '../../test/utils';
import DashboardTabs from './DashboardTabs';
import type { Sphere } from './spheres';
import type { Dashboard } from '../../api/dashboards';

const spheres: Sphere[] = [
  { id: 'sphere:light', category: 'light', count: 2 },
  { id: 'sphere:sensor', category: 'sensor', count: 5 },
];

const dashboards: Dashboard[] = [
  {
    id: 'dash-1', name: 'Гостиная', icon: 'sofa', order: 0, sections: [],
    createdAt: '2026-07-06T00:00:00Z', updatedAt: '2026-07-06T00:00:00Z',
  },
];

const noop = () => undefined;

const renderTabs = (over: Partial<Parameters<typeof DashboardTabs>[0]> = {}) =>
  render(
    <DashboardTabs
      spheres={spheres}
      allSpheres={spheres}
      dashboards={dashboards}
      hiddenSpheres={[]}
      activeId="all"
      onSelect={noop}
      onCreate={noop}
      onEdit={noop}
      onToggleSphere={noop}
      onReorder={noop}
      {...over}
    />,
  );

describe('DashboardTabs', () => {
  it('renders All, derived spheres with counts, and custom tabs', () => {
    renderTabs();
    expect(screen.getByRole('tab', { name: 'Все' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Свет · 2/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Датчики · 5/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Гостиная/ })).toBeInTheDocument();
  });

  it('fires onSelect with the tab id on click', () => {
    const onSelect = vi.fn();
    renderTabs({ onSelect });
    fireEvent.click(screen.getByRole('tab', { name: /Свет · 2/ }));
    expect(onSelect).toHaveBeenCalledWith('sphere:light');
  });

  it('does not render a hidden sphere as a tab', () => {
    renderTabs({ spheres: [spheres[1]], hiddenSpheres: ['light'] });
    expect(screen.queryByRole('tab', { name: /Свет/ })).not.toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Датчики · 5/ })).toBeInTheDocument();
  });

  it('opens the manage menu with sphere visibility toggles', () => {
    const onToggleSphere = vi.fn();
    renderTabs({ onToggleSphere, hiddenSpheres: ['sensor'] });
    fireEvent.click(screen.getByRole('button', { name: 'Управление вкладками' }));
    // "Датчики" is hidden → its toggle re-shows it.
    fireEvent.click(screen.getByRole('menuitem', { name: /Датчики/ }));
    expect(onToggleSphere).toHaveBeenCalledWith('sensor', false);
  });

  it('offers a new-tab action', () => {
    const onCreate = vi.fn();
    renderTabs({ onCreate });
    fireEvent.click(screen.getByRole('button', { name: 'Новая вкладка' }));
    expect(onCreate).toHaveBeenCalled();
  });
});
