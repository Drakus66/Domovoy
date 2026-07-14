// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../test/utils';
import Settings from './Settings';
import { settingsApi } from '../api/settings';

// The Leaflet map needs a real DOM with sizing; stub it so the page renders under jsdom.
vi.mock('../components/settings/LocationMap', () => ({
  default: () => <div data-testid="location-map" />,
}));

vi.mock('../api/settings', () => ({
  settingsApi: {
    getLocation: vi.fn(),
    saveLocation: vi.fn(),
    getTimezone: vi.fn(),
    geocode: vi.fn(),
    reverseGeocode: vi.fn(),
    getCalendar: vi.fn(),
    saveCalendar: vi.fn(),
    importHolidays: vi.fn(),
  },
}));

const mockedApi = vi.mocked(settingsApi);

const sampleLocation = {
  id: 'current',
  latitude: 55.7558,
  longitude: 37.6173,
  label: 'Moscow',
  timeZoneId: 'Europe/Moscow',
  timeZoneAuto: true,
  updatedAt: '2026-07-06T00:00:00Z',
};

describe('Settings page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedApi.getLocation.mockResolvedValue(sampleLocation);
    mockedApi.getTimezone.mockResolvedValue('Europe/Moscow');
    mockedApi.geocode.mockResolvedValue([]);
    mockedApi.saveLocation.mockResolvedValue(sampleLocation);
    mockedApi.getCalendar.mockResolvedValue({
      id: 'current', weekendDays: [6, 0], holidays: ['2026-01-01'], updatedAt: '2026-07-06T00:00:00Z',
    });
    mockedApi.saveCalendar.mockResolvedValue({
      id: 'current', weekendDays: [6, 0], holidays: ['2026-01-01'], updatedAt: '2026-07-06T00:00:00Z',
    });
  });

  it('loads and shows the persisted location', async () => {
    render(<Settings />);
    await waitFor(() => expect(mockedApi.getLocation).toHaveBeenCalled());
    expect(await screen.findByDisplayValue('55.7558')).toBeInTheDocument();
    expect(screen.getByDisplayValue('37.6173')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Moscow')).toBeInTheDocument();
    expect(screen.getByTestId('location-map')).toBeInTheDocument();
  });

  it('saves the edited location', async () => {
    render(<Settings />);
    await waitFor(() => expect(mockedApi.getLocation).toHaveBeenCalled());

    // Sections are collapsed by default — open Location to reach its save button.
    fireEvent.click(screen.getByText('Местоположение'));
    const saveButton = await screen.findByRole('button', { name: /Сохранить местоположение/i });
    fireEvent.click(saveButton);

    await waitFor(() =>
      expect(mockedApi.saveLocation).toHaveBeenCalledWith(
        expect.objectContaining({ latitude: 55.7558, longitude: 37.6173, timeZoneAuto: true }),
      ),
    );
  });

  it('loads calendar settings and shows the holiday chips', async () => {
    render(<Settings />);
    await waitFor(() => expect(mockedApi.getCalendar).toHaveBeenCalled());
    expect(await screen.findByText('2026-01-01')).toBeInTheDocument();
  });

  it('saves the calendar settings', async () => {
    render(<Settings />);
    await waitFor(() => expect(mockedApi.getCalendar).toHaveBeenCalled());

    // Sections are collapsed by default — open Calendar to reach its save button.
    fireEvent.click(screen.getByText('Календарь'));
    const saveButton = await screen.findByRole('button', { name: /Сохранить календарь/i });
    fireEvent.click(saveButton);

    await waitFor(() =>
      expect(mockedApi.saveCalendar).toHaveBeenCalledWith(
        expect.objectContaining({ weekendDays: [6, 0], holidays: ['2026-01-01'] }),
      ),
    );
  });

  it('geocodes a place search into results', async () => {
    mockedApi.geocode.mockResolvedValueOnce([
      { label: 'Berlin, Germany', latitude: 52.52, longitude: 13.405 },
    ]);
    render(<Settings />);
    await waitFor(() => expect(mockedApi.getLocation).toHaveBeenCalled());

    const search = screen.getByLabelText(/Поиск места/i);
    fireEvent.change(search, { target: { value: 'Berlin' } });
    fireEvent.keyDown(search, { key: 'Enter' });

    expect(await screen.findByText('Berlin, Germany')).toBeInTheDocument();
  });
});
