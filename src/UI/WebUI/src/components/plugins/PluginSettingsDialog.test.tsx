// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../../test/utils';
import PluginSettingsDialog from './PluginSettingsDialog';
import { pluginsApi } from '../../api/plugins';

vi.mock('../../api/plugins', () => ({
  pluginsApi: {
    getSettings: vi.fn(),
    updateSettings: vi.fn(),
  },
}));

const mockedApi = vi.mocked(pluginsApi);

const schema = {
  settings: [
    { key: 'provider', kind: 'enum', label: 'Источник трафика', values: ['simulated', 'tomtom'], default: 'simulated' },
    { key: 'tomTomApiKey', kind: 'secret', label: 'Ключ TomTom API', secret: true },
    { key: 'avgSpeedKmh', kind: 'number', label: 'Средняя скорость', unit: 'км/ч', min: 10, max: 130, step: 1, default: 45 },
  ],
  values: { provider: 'simulated', tomTomApiKey: '', avgSpeedKmh: 45 },
};

describe('PluginSettingsDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedApi.getSettings.mockResolvedValue(schema);
    mockedApi.updateSettings.mockResolvedValue({ result: 'applied' });
  });

  it('renders a control per setting from the announced schema', async () => {
    render(<PluginSettingsDialog open pluginId="commute-planner" pluginName="Commute" onClose={() => {}} />);
    await waitFor(() => expect(mockedApi.getSettings).toHaveBeenCalledWith('commute-planner'));

    expect(await screen.findByText('Источник трафика')).toBeInTheDocument();
    expect(screen.getByText('Ключ TomTom API')).toBeInTheDocument();
    // The number setting shows its label + unit.
    expect(screen.getByText(/Средняя скорость/)).toBeInTheDocument();
  });

  it('saves the current values and closes', async () => {
    const onClose = vi.fn();
    render(<PluginSettingsDialog open pluginId="commute-planner" pluginName="Commute" onClose={onClose} />);
    await waitFor(() => expect(mockedApi.getSettings).toHaveBeenCalled());

    fireEvent.click(await screen.findByRole('button', { name: /Сохранить/i }));

    await waitFor(() =>
      expect(mockedApi.updateSettings).toHaveBeenCalledWith(
        'commute-planner',
        expect.objectContaining({ provider: 'simulated', avgSpeedKmh: 45 }),
      ),
    );
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });
});
