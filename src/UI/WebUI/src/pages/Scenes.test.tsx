// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../test/utils';
import Scenes from './Scenes';
import { scenesApi, Scene } from '../api/scenes';
import { capabilityDevicesApi, CapabilityDevice } from '../api/capabilityDevices';

vi.mock('../api/scenes', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/scenes')>()),
  scenesApi: {
    getScenes: vi.fn(),
    getScene: vi.fn(),
    createScene: vi.fn(),
    updateScene: vi.fn(),
    deleteScene: vi.fn(),
    activate: vi.fn(),
  },
}));
vi.mock('../api/capabilityDevices', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/capabilityDevices')>()),
  capabilityDevicesApi: { getDevices: vi.fn() },
}));

const mockedScenes = vi.mocked(scenesApi);
const mockedDevices = vi.mocked(capabilityDevicesApi);

// A lamp with two writable capabilities and one read-only one — the read-only value must NOT be captured.
const lamp: CapabilityDevice = {
  id: 'd1', name: 'Лампа', adapterSource: 'Zigbee2Mqtt', zoneId: '', isOnline: true, lastUpdated: '',
  capabilities: [
    { id: 'on_off', kind: 'Boolean', writable: true },
    { id: 'brightness', kind: 'Number', writable: true, min: 0, max: 100 },
    { id: 'temperature', kind: 'Number', writable: false },
  ],
  state: { on_off: true, brightness: 40, temperature: 22 },
};

const scene: Scene = {
  id: 's1', name: 'Вечер', description: null, icon: null,
  targets: [{ deviceId: 'd1', set: { on_off: true } }],
  createdAt: '2026-07-19T00:00:00Z', updatedAt: '2026-07-19T00:00:00Z',
};

beforeEach(() => {
  vi.clearAllMocks();
  mockedDevices.getDevices.mockResolvedValue([lamp]);
});

describe('Scenes page', () => {
  it('shows the empty state when there are no scenes', async () => {
    mockedScenes.getScenes.mockResolvedValue([]);
    render(<Scenes />);
    expect(await screen.findByText(/Сцен пока нет/)).toBeInTheDocument();
  });

  it('lists a scene and activates it, confirming with a toast', async () => {
    mockedScenes.getScenes.mockResolvedValue([scene]);
    mockedScenes.activate.mockResolvedValue(undefined);
    render(<Scenes />);

    expect(await screen.findByText('Вечер')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Включить'));

    await waitFor(() => expect(mockedScenes.activate).toHaveBeenCalledWith('s1'));
    expect(await screen.findByText(/Сцена «Вечер» включена/)).toBeInTheDocument();
  });

  it('captures only writable state and saves a new scene', async () => {
    mockedScenes.getScenes.mockResolvedValue([]);
    mockedScenes.createScene.mockResolvedValue({ ...scene, id: 's2' });
    render(<Scenes />);
    await screen.findByText(/Сцен пока нет/);

    fireEvent.click(screen.getByText('Новая сцена'));
    fireEvent.click(screen.getByText('Снять с устройств'));

    // Pick the lamp in the capture dialog, then add it.
    fireEvent.click(await screen.findByText('Лампа'));
    fireEvent.click(screen.getByText('Добавить (1)'));

    // Name the scene and create it.
    fireEvent.change(screen.getByLabelText(/Название/), { target: { value: 'Ночь' } });
    fireEvent.click(screen.getByText('Создать'));

    await waitFor(() => expect(mockedScenes.createScene).toHaveBeenCalled());
    const payload = mockedScenes.createScene.mock.calls[0][0];
    expect(payload.name).toBe('Ночь');
    expect(payload.targets).toHaveLength(1);
    // temperature (read-only) is excluded; only writable capabilities are snapshotted.
    expect(payload.targets[0].set).toEqual({ on_off: true, brightness: 40 });
  });
});
