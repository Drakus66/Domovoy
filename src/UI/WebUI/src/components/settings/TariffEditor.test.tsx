// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../../test/utils';
import TariffEditor from './TariffEditor';
import { settingsApi } from '../../api/settings';

vi.mock('../../api/settings', () => ({
  settingsApi: {
    getTariff: vi.fn(),
    saveTariff: vi.fn(),
  },
}));

const mocked = vi.mocked(settingsApi);

describe('TariffEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocked.getTariff.mockResolvedValue({
      id: 'current', currency: '₽', defaultPrice: 5,
      zones: [{ name: 'night', pricePerKwh: 2, intervals: [{ startMinute: 1380, endMinute: 420 }] }],
      updatedAt: '',
    });
    mocked.saveTariff.mockResolvedValue({
      id: 'current', currency: '₽', defaultPrice: 5, zones: [], updatedAt: '',
    });
  });

  it('loads the tariff and renders the zone with its window', async () => {
    render(<TariffEditor />);
    await waitFor(() => expect(mocked.getTariff).toHaveBeenCalled());
    expect(await screen.findByDisplayValue('night')).toBeInTheDocument();
    expect(screen.getByDisplayValue('23:00')).toBeInTheDocument(); // startMinute 1380 → 23:00
    expect(screen.getByDisplayValue('07:00')).toBeInTheDocument(); // endMinute 420 → 07:00
  });

  it('saves the tariff', async () => {
    render(<TariffEditor />);
    await waitFor(() => expect(mocked.getTariff).toHaveBeenCalled());
    await screen.findByDisplayValue('night');

    fireEvent.click(screen.getByRole('button', { name: /Сохранить тариф/i }));

    await waitFor(() =>
      expect(mocked.saveTariff).toHaveBeenCalledWith(
        expect.objectContaining({
          currency: '₽',
          defaultPrice: 5,
          zones: expect.arrayContaining([expect.objectContaining({ name: 'night', pricePerKwh: 2 })]),
        }),
      ),
    );
  });
});
