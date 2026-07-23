// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../test/utils';
import Settings from './Settings';
import { settingsApi } from '../api/settings';
import { backupsApi } from '../api/backups';

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
    getTariff: vi.fn(),
    saveTariff: vi.fn(),
  },
}));

vi.mock('../api/backups', () => ({
  backupsApi: {
    getSettings: vi.fn(),
    saveSettings: vi.fn(),
    list: vi.fn(),
    runNow: vi.fn(),
    restore: vi.fn(),
    remove: vi.fn(),
    downloadUrl: vi.fn(() => '/api/backup/x/download'),
    upload: vi.fn(),
  },
}));

const mockedApi = vi.mocked(settingsApi);
const mockedBackups = vi.mocked(backupsApi);

const sampleBackupSettings = {
  id: 'current',
  enabled: true,
  time: '03:30',
  keepCount: 7,
  lastRunAt: '2026-07-19T00:30:00Z',
  lastResult: 'ok' as const,
  lastError: null,
  lastFile: 'domovoy-backup-20260719-003000.zip',
  updatedAt: '2026-07-19T00:30:00Z',
};

const sampleBackup = {
  fileName: 'domovoy-backup-20260719-003000.zip',
  sizeBytes: 4 * 1024 * 1024,
  createdAt: '2026-07-19T00:30:00Z',
  reason: 'scheduled',
  collections: 20,
  documents: 1234,
  valid: true,
};

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
    mockedApi.getTariff.mockResolvedValue({
      id: 'current', currency: '₽', defaultPrice: 5, zones: [], updatedAt: '2026-07-20T00:00:00Z',
    });
    mockedApi.saveTariff.mockResolvedValue({
      id: 'current', currency: '₽', defaultPrice: 5, zones: [], updatedAt: '2026-07-20T00:00:00Z',
    });
    mockedBackups.getSettings.mockResolvedValue(sampleBackupSettings);
    mockedBackups.saveSettings.mockResolvedValue(sampleBackupSettings);
    mockedBackups.list.mockResolvedValue([sampleBackup]);
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

  it('lists backup bundles with the schedule loaded', async () => {
    render(<Settings />);
    await waitFor(() => expect(mockedBackups.getSettings).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Бэкапы'));
    expect(await screen.findByText('domovoy-backup-20260719-003000.zip')).toBeInTheDocument();
    expect(screen.getByDisplayValue('03:30')).toBeInTheDocument();
  });

  it('saves the backup schedule', async () => {
    render(<Settings />);
    await waitFor(() => expect(mockedBackups.getSettings).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Бэкапы'));
    const saveButton = await screen.findByRole('button', { name: /Сохранить расписание/i });
    fireEvent.click(saveButton);

    await waitFor(() =>
      expect(mockedBackups.saveSettings).toHaveBeenCalledWith(
        expect.objectContaining({ enabled: true, time: '03:30', keepCount: 7 }),
      ),
    );
  });

  it('restores a bundle after confirmation', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    mockedBackups.restore.mockResolvedValue({
      restored: sampleBackup.fileName, collections: 20, documents: 1234,
      pluginSettings: 1, extrasStagingDirectory: null, restarting: true,
    });
    render(<Settings />);
    await waitFor(() => expect(mockedBackups.list).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Бэкапы'));
    const restoreButton = await screen.findAllByRole('button', { name: /Восстановить/i });
    fireEvent.click(restoreButton[0]);

    await waitFor(() => expect(mockedBackups.restore).toHaveBeenCalledWith(sampleBackup.fileName));
    expect(confirmSpy).toHaveBeenCalled();
    confirmSpy.mockRestore();
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
