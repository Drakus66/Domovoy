// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../../test/utils';
import LoadManagementEditor from './LoadManagementEditor';
import { loadManagementApi } from '../../api/loadManagement';

vi.mock('../../api/loadManagement', () => ({
  loadManagementApi: {
    getSettings: vi.fn(),
    saveSettings: vi.fn(),
  },
  POWER_SOURCES: ['grid', 'grid_peak', 'battery', 'solar', 'off'],
  SINGLE_PHASES: ['l1', 'l2', 'l3'],
}));

const mocked = vi.mocked(loadManagementApi);

describe('LoadManagementEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocked.getSettings.mockResolvedValue({
      id: 'current', enabled: true,
      budgets: [{ powerSource: 'grid', limitWatts: 5000 }],
      restoreMarginWatts: 100, minDwellSeconds: 120, updatedAt: '',
    });
    mocked.saveSettings.mockResolvedValue({
      id: 'current', enabled: true, budgets: [], restoreMarginWatts: 100, minDwellSeconds: 120, updatedAt: '',
    });
  });

  it('loads settings and renders the configured budget', async () => {
    render(<LoadManagementEditor />);
    await waitFor(() => expect(mocked.getSettings).toHaveBeenCalled());
    expect(await screen.findByDisplayValue('5000')).toBeInTheDocument();
  });

  it('saves the settings with the current form state', async () => {
    render(<LoadManagementEditor />);
    await waitFor(() => expect(mocked.getSettings).toHaveBeenCalled());
    await screen.findByDisplayValue('5000');

    fireEvent.click(screen.getByRole('button', { name: /Сохранить/i }));

    await waitFor(() =>
      expect(mocked.saveSettings).toHaveBeenCalledWith(
        expect.objectContaining({
          enabled: true,
          budgets: expect.arrayContaining([expect.objectContaining({ powerSource: 'grid', limitWatts: 5000 })]),
          restoreMarginWatts: 100,
          minDwellSeconds: 120,
        }),
      ),
    );
  });

  it('adds a new budget row for an unused power source', async () => {
    render(<LoadManagementEditor />);
    await waitFor(() => expect(mocked.getSettings).toHaveBeenCalled());
    await screen.findByDisplayValue('5000');

    fireEvent.click(screen.getByRole('button', { name: /Добавить бюджет/i }));

    expect(await screen.findByDisplayValue('0')).toBeInTheDocument(); // new row defaults to 0 W
  });
});
