// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../../../test/utils';
import EnergyTile from './EnergyTile';
import { energyApi } from '../../../api/energy';

vi.mock('../../../api/energy', () => ({
  energyApi: {
    getConsumption: vi.fn(),
    getCost: vi.fn(),
    getBreakdown: vi.fn(),
  },
}));

const mocked = vi.mocked(energyApi);

describe('EnergyTile', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocked.getConsumption.mockResolvedValue({
      from: '', to: '', bucket: 'hour',
      devices: [
        { deviceId: 'a', name: 'Boiler', zoneId: '', archetype: 'energy_meter', energyRole: null, kwh: 5 },
        { deviceId: 'b', name: 'Fridge', zoneId: '', archetype: 'energy_meter', energyRole: null, kwh: 1 },
        { deviceId: 'm', name: 'Mains', zoneId: '', archetype: 'energy_meter', energyRole: 'mains', kwh: 7 },
      ],
      consumerTotalKwh: 6, mainsTotalKwh: 7,
    });
    mocked.getCost.mockResolvedValue({
      from: '', to: '', currency: '₽', totalKwh: 6, totalCost: 30,
      zones: [{ zone: 'default', kwh: 6, cost: 30 }],
    });
    mocked.getBreakdown.mockResolvedValue({
      from: '', to: '',
      nodes: [{
        nodeId: 'c1', name: 'Кухня', kind: 'circuit', parentId: 'p1', phase: 'l1',
        kwh: 5, powerW: 300, deviceCount: 1, meterKwh: 6, meterPowerW: 350, unaccountedKwh: 1, limitWatts: 3680,
      }],
      phases: [{ phase: 'l1', kwh: 5, powerW: 300, limitWatts: 3680 }],
      unmappedKwh: 1, unmappedPowerW: 0,
    });
  });

  it('shows the period cost and the top consumers', async () => {
    render(<EnergyTile />);
    await waitFor(() => expect(mocked.getConsumption).toHaveBeenCalled());

    expect(await screen.findByText('Boiler')).toBeInTheDocument();
    expect(screen.getByText('Fridge')).toBeInTheDocument();
    expect(screen.getByText('Mains')).toBeInTheDocument(); // the aggregate meter is listed, flagged as mains
    expect(screen.getByText(/30\.00\s*₽/)).toBeInTheDocument(); // total cost for the period
  });

  it('switches to the per-circuit view and reports the unaccounted remainder', async () => {
    render(<EnergyTile />);
    await waitFor(() => expect(mocked.getBreakdown).toHaveBeenCalled());

    fireEvent.click(await screen.findByRole('button', { name: 'Линии' }));

    expect(await screen.findByText('Кухня')).toBeInTheDocument();
    // 1 kWh the circuit meter saw but no device explains + 1 kWh from devices with no circuit.
    expect(screen.getByText(/Не отнесено к устройствам: 2\.00/)).toBeInTheDocument();
  });
});
