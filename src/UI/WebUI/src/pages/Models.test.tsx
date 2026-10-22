// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../test/utils';
import Models from './Models';
import { mlApi, MlTask, MlModel } from '../api/ml';
import { blocksApi, BlockCatalogEntry, ControlBlock } from '../api/blocks';
import { capabilityDevicesApi } from '../api/capabilityDevices';
import { zonesApi } from '../api/zones';

vi.mock('../api/ml', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/ml')>()),
  mlApi: {
    getTasks: vi.fn(),
    getModels: vi.fn(),
    createTask: vi.fn(),
    updateTask: vi.fn(),
    deleteTask: vi.fn(),
    dataCheck: vi.fn(),
    train: vi.fn(),
    backtest: vi.fn(),
    deleteModel: vi.fn(),
    pruneModels: vi.fn(),
    classifyArchetypes: vi.fn(),
    // Epic 3I: the Models page renders MlLayerControls (getSettings) + a Journal tab (getActivity).
    getActivity: vi.fn().mockResolvedValue([]),
    getSettings: vi.fn().mockResolvedValue({ enabled: true, proposalsEnabled: true, minHistoryDays: 7 }),
    saveSettings: vi.fn(),
  },
}));
vi.mock('../api/blocks', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/blocks')>()),
  blocksApi: {
    getBlocks: vi.fn(),
    getCatalog: vi.fn(),
    getStatus: vi.fn(),
    createBlock: vi.fn(),
    updateBlock: vi.fn(),
    deleteBlock: vi.fn(),
  },
}));
vi.mock('../api/capabilityDevices', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/capabilityDevices')>()),
  capabilityDevicesApi: { getDevices: vi.fn(), sendCommand: vi.fn(), assignZone: vi.fn(), setArchetype: vi.fn() },
}));
vi.mock('../api/zones', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/zones')>()),
  zonesApi: { getZones: vi.fn() },
}));

const mockedMl = vi.mocked(mlApi);
const mockedBlocks = vi.mocked(blocksApi);
const mockedDevices = vi.mocked(capabilityDevicesApi);
const mockedZones = vi.mocked(zonesApi);

const task: MlTask = {
  id: 'default',
  name: 'temperature',
  targetCapability: 'temperature',
  enabled: true,
  windowDays: 30,
  minSamples: 20,
  trainIntervalHours: 24,
  trainZoneModels: true,
  zonePromotionMargin: 0.25,
  clampMin: 16,
  clampMax: 26,
  keepLastVersions: 10,
  createdAt: '2026-07-01T00:00:00Z',
  updatedAt: '2026-07-01T00:00:00Z',
  status: {
    lastTrainAt: '2026-07-08T00:00:00Z',
    lastTrainOk: false,
    lastMessage: 'not enough data (12/20)',
    lastSampleCount: 12,
    lastRegisteredScopes: 0,
  },
};

const model: MlModel = {
  id: 'm1', name: 'temperature schedule_regression [global]', kind: 'schedule_regression',
  targetCapability: 'temperature', scope: { level: 'global', key: '' }, version: 3,
  trainedAt: '2026-07-08T00:00:00Z', sampleCount: 1000, rmse: 0.5,
  holdoutMae: 0.4, holdoutSampleCount: 200, holdoutScore: 0.4, metric: 'MAE', features: 'time',
};

const governorType: BlockCatalogEntry = {
  typeId: 'ml_thermostat', title: 'ML thermostat', description: '',
  mlTargetCapability: 'temperature',
  inputs: [{ name: 'temperature', kind: 'Number', description: '' }],
  outputs: [{ id: 'temperature_setpoint', kind: 'Number', writable: true }],
  params: [{ name: 'stage', default: 0, description: '' }],
};

const consumer = {
  id: 'b1', name: 'Кухня: ML-термостат', typeId: 'ml_thermostat', deviceId: 'vd1',
  enabled: true, params: { stage: 1, model_version: 0 }, inputs: {}, outputs: {},
  createdAt: '', updatedAt: '',
} as ControlBlock;

describe('ML hub (Models page, Epic 2P)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedMl.getTasks.mockResolvedValue([task]);
    mockedMl.getModels.mockResolvedValue([model]);
    mockedMl.dataCheck.mockResolvedValue({
      target: 'temperature', windowDays: 30, minSamples: 20, kind: 'Number', templateAvailable: true, scopes: [],
    });
    mockedMl.train.mockResolvedValue([{ taskId: 'default', target: 'temperature', result: { trained: true, message: 'ok' } }]);
    mockedBlocks.getCatalog.mockResolvedValue([governorType]);
    mockedBlocks.getBlocks.mockResolvedValue([consumer]);
    mockedDevices.getDevices.mockResolvedValue([]);
    mockedZones.getZones.mockResolvedValue([]);
  });

  it('renders a task card with target, quality, diagnostics and its consumer with a stage chip', async () => {
    render(<Models />);

    // Task header: name + target chip render (name and target coincide here) + honest holdout quality.
    expect((await screen.findAllByText('temperature')).length).toBeGreaterThan(0);
    expect(screen.getByText('MAE 0.40')).toBeInTheDocument();

    // "Why it didn't train" — the trainer's last message is right on the card.
    expect(screen.getByText(/not enough data \(12\/20\)/)).toBeInTheDocument();

    // The consumer block with its authority stage (Bounded).
    expect(screen.getByText('Кухня: ML-термостат')).toBeInTheDocument();
    expect(screen.getByText('Ограниченно')).toBeInTheDocument();
  });

  it('trains a single task via its train button', async () => {
    render(<Models />);
    await screen.findAllByText('temperature');

    fireEvent.click(screen.getByRole('button', { name: 'Обучить эту задачу сейчас' }));

    await waitFor(() => expect(mockedMl.train).toHaveBeenCalledWith('default'));
  });

  it('toggling enabled saves the task', async () => {
    render(<Models />);
    await screen.findAllByText('temperature');
    mockedMl.updateTask.mockResolvedValue(task);

    // The task card's enable switch (scoped by its aria-label — the ML pulse panel adds its own switches).
    fireEvent.click(screen.getByRole('checkbox', { name: /обучение/ }));

    await waitFor(() => expect(mockedMl.updateTask).toHaveBeenCalledWith(
      'default', expect.objectContaining({ enabled: false })));
  });

  it('changing the consumer stage asks for explicit confirmation before saving', async () => {
    render(<Models />);
    await screen.findByText('Кухня: ML-термостат');
    mockedBlocks.updateBlock.mockResolvedValue(undefined);

    // Click the stage chip → dialog → pick Full → confirm.
    fireEvent.click(screen.getByText('Ограниченно'));
    expect(await screen.findByText(/Полномочия блока/)).toBeInTheDocument();
    fireEvent.mouseDown(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByRole('option', { name: 'Полностью' }));
    fireEvent.click(screen.getByRole('button', { name: 'Подтвердить' }));

    await waitFor(() => expect(mockedBlocks.updateBlock).toHaveBeenCalledWith(
      'b1', expect.objectContaining({ params: expect.objectContaining({ stage: 2 }) })));
  });

  it('shows the empty state with a create call-to-action when no tasks exist', async () => {
    mockedMl.getTasks.mockResolvedValue([]);
    render(<Models />);

    expect(await screen.findByText('Задач обучения пока нет.')).toBeInTheDocument();
  });

  it('shows the activity feed on the Journal tab (Epic 3I)', async () => {
    mockedMl.getActivity.mockResolvedValue([
      { id: '1', timestamp: new Date().toISOString(), source: 'discovery', outcome: 'ok', reason: 'created', metrics: { created: 2 } },
    ]);
    render(<Models />);
    await screen.findAllByText('temperature');

    fireEvent.click(screen.getByRole('tab', { name: 'Журнал' }));

    expect(await screen.findByText('Добавлено предложений: 2')).toBeInTheDocument();
  });
});
