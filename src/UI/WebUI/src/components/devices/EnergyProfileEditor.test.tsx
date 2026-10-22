// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../../test/utils';
import EnergyProfileEditor from './EnergyProfileEditor';
import { energyApi } from '../../api/energy';
import type { CapabilityDevice } from '../../api/capabilityDevices';

vi.mock('../../api/energy', () => ({
  energyApi: { setEnergyProfile: vi.fn().mockResolvedValue(undefined) },
}));

vi.mock('../../api/history', () => ({
  historyApi: { getAggregate: vi.fn().mockResolvedValue([]) },
}));

vi.mock('../../api/powerTopology', () => ({
  powerTopologyApi: { getNodes: vi.fn().mockResolvedValue([]) },
}));

const mocked = vi.mocked(energyApi);

const lamp: CapabilityDevice = {
  id: 'lamp-1',
  name: 'Lamp',
  adapterSource: 'Zigbee2Mqtt',
  zoneId: 'z',
  autoArchetype: 'light',
  capabilities: [
    { id: 'on_off', kind: 'Boolean', writable: true },
    { id: 'brightness', kind: 'Number', writable: true },
  ],
  state: {},
  isOnline: true,
  lastUpdated: '',
};

describe('EnergyProfileEditor', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('renders nothing for a plain sensor (nothing to control, nothing measured)', () => {
    const sensor: CapabilityDevice = {
      ...lamp, capabilities: [{ id: 'temperature', kind: 'Number', writable: false }],
    };
    const { container } = render(<EnergyProfileEditor device={sensor} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('turns accounting on with an archetype-suggested power and saves the profile', async () => {
    render(<EnergyProfileEditor device={lamp} />);

    // Off by default for an unmetered device — only the toggle is shown.
    expect(screen.queryByLabelText('Мощность')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('checkbox', { name: /Учитывать в потреблении/i }));

    // A light pre-fills 9 W as a starting point, and gets the regulator picker (its brightness).
    const power = await screen.findByLabelText('Мощность');
    expect(power).toHaveValue(9);
    expect(screen.getByLabelText('Регулятор мощности')).toBeInTheDocument();

    fireEvent.change(power, { target: { value: '60' } });
    fireEvent.click(screen.getByRole('button', { name: /Сохранить/i }));

    await waitFor(() =>
      expect(mocked.setEnergyProfile).toHaveBeenCalledWith(
        'lamp-1', expect.objectContaining({ track: true, maxPowerW: 60 }),
      ),
    );
  });

  it('hides the estimation fields for a metered device and defaults to counting it', () => {
    const plug: CapabilityDevice = {
      ...lamp,
      id: 'plug-1',
      capabilities: [
        { id: 'on_off', kind: 'Boolean', writable: true },
        { id: 'energy', kind: 'Number', writable: false, unit: 'kWh' },
      ],
    };
    render(<EnergyProfileEditor device={plug} />);

    expect(screen.getByRole('checkbox', { name: /Учитывать в потреблении/i })).toBeChecked();
    expect(screen.queryByLabelText(/Регулятор мощности/)).not.toBeInTheDocument();
    expect(screen.getByLabelText(/Роль в энергоучёте/)).toBeInTheDocument();
  });
});
