// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../test/utils';
import Proposals from './Proposals';
import { proposalsApi, Proposal } from '../api/proposals';
import { automationsApi, AutomationRule } from '../api/automations';
import { capabilityDevicesApi, CapabilityDevice } from '../api/capabilityDevices';
import { replayApi } from '../api/replay';

vi.mock('../api/proposals', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/proposals')>()),
  proposalsApi: {
    list: vi.fn(),
    create: vi.fn(),
    approve: vi.fn(),
    reject: vi.fn(),
    suggest: vi.fn(),
    discover: vi.fn(),
    suggestTasks: vi.fn(),
  },
}));
vi.mock('../api/automations', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/automations')>()),
  automationsApi: { getRules: vi.fn(), getRule: vi.fn() },
}));
vi.mock('../api/capabilityDevices', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/capabilityDevices')>()),
  capabilityDevicesApi: { getDevices: vi.fn() },
}));
vi.mock('../api/replay', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/replay')>()),
  replayApi: { run: vi.fn() },
}));

const mockedProposals = vi.mocked(proposalsApi);
const mockedAutomations = vi.mocked(automationsApi);
const mockedDevices = vi.mocked(capabilityDevicesApi);
const mockedReplay = vi.mocked(replayApi);

const device = (id: string, name: string): CapabilityDevice => ({
  id, name, adapterSource: 'Emulator', zoneId: '', capabilities: [], state: {},
  isOnline: true, lastUpdated: '2026-07-10T00:00:00Z',
});

const rule: AutomationRule = {
  id: 'r1',
  name: 'Turn on "Лампа" when motion detected by "Датчик"',
  status: 'Proposed',
  isProtected: false,
  triggers: [{ type: 'DeviceState', deviceId: 'dev-motion', capabilityId: 'motion', operator: 'eq', value: true }],
  conditions: [],
  actions: [{ type: 'Command', deviceId: 'dev-lamp', set: { on_off: true } }],
  createdAt: '2026-07-10T00:00:00Z',
  updatedAt: '2026-07-10T00:00:00Z',
};

const ruleProposal: Proposal = {
  id: 'p1',
  kind: 'Rule',
  status: 'Proposed',
  title: 'Turn on "Лампа" when motion detected by "Датчик"',
  rationale: 'Seen 14× in 7d, confidence 86% (P(action|trigger)).',
  source: 'ml_proposer',
  ruleId: 'r1',
  evidence: { support: 14, confidence: 0.86, windowDays: 7 },
  decisionId: '',
  createdAt: '2026-07-10T12:00:00Z',
};

const promotionProposal: Proposal = {
  id: 'p2',
  kind: 'BlockPromotion',
  status: 'Proposed',
  title: 'Повысить «ML-термостат: гостиная»',
  source: 'user',
  blockId: 'b1',
  fromStage: 0,
  toStage: 1,
  decisionId: '',
  createdAt: '2026-07-10T12:00:00Z',
};

const mlTaskProposal: Proposal = {
  id: 'p3',
  kind: 'MlTask',
  status: 'Proposed',
  title: "Start learning 'humidity'",
  source: 'ml_task_scanner',
  mlTaskTarget: 'humidity',
  evidence: { samples: 480, required: 20, windowDays: 30 },
  decisionId: '',
  createdAt: '2026-07-10T12:00:00Z',
};

beforeEach(() => {
  vi.clearAllMocks();
  mockedProposals.list.mockResolvedValue([ruleProposal, promotionProposal, mlTaskProposal]);
  mockedAutomations.getRules.mockResolvedValue([rule]);
  mockedDevices.getDevices.mockResolvedValue([device('dev-motion', 'Датчик прихожей'), device('dev-lamp', 'Лампа прихожей')]);
});

describe('Proposals page', () => {
  it('renders a rule proposal as a localized sentence built from the rule itself', async () => {
    render(<Proposals />);
    // Trigger and action device names resolved, sentence in Russian — not the server's English title.
    expect(await screen.findByText(/Если .*Датчик прихожей.* — включить «Лампа прихожей»/)).toBeInTheDocument();
    expect(screen.queryByText(ruleProposal.title)).not.toBeInTheDocument();
  });

  it('renders localized evidence from structured numbers instead of the English rationale', async () => {
    render(<Proposals />);
    expect(await screen.findByText(/За последние 7 дн\. так происходило 14 раз: в 86% случаев/)).toBeInTheDocument();
    expect(screen.queryByText(/Seen 14×/)).not.toBeInTheDocument();
  });

  it('spells out the concrete approve side-effect per kind', async () => {
    render(<Proposals />);
    // BlockPromotion: from/to stages, human-named.
    expect(await screen.findByText(/блок получит стадию «Ограниченно» \(сейчас «Наблюдение»\)/)).toBeInTheDocument();
    // MlTask: a training task appears, with the localized title.
    expect(screen.getByText('Начать обучение: Влажность')).toBeInTheDocument();
    expect(screen.getByText(/появится задача обучения/)).toBeInTheDocument();
  });

  it('shows humanized source chips', async () => {
    render(<Proposals />);
    expect(await screen.findByText('сканер журнала')).toBeInTheDocument();
    expect(screen.getByText('сканер ML-задач')).toBeInTheDocument();
    expect(screen.getByText('вы')).toBeInTheDocument();
  });

  it('"Check now" runs all three scanners and reports a combined result', async () => {
    mockedProposals.suggest.mockResolvedValue({ candidates: 3, created: 1, note: 'ok' });
    mockedProposals.discover.mockResolvedValue({ patterns: 2, created: 2, note: 'ok' });
    mockedProposals.suggestTasks.mockResolvedValue({ candidates: 1, created: 1, note: 'ok' });
    render(<Proposals />);
    await screen.findByText('сканер журнала');

    fireEvent.click(screen.getByText('Проверить сейчас'));

    await waitFor(() => {
      expect(mockedProposals.suggest).toHaveBeenCalled();
      expect(mockedProposals.discover).toHaveBeenCalled();
      expect(mockedProposals.suggestTasks).toHaveBeenCalled();
    });
    expect(await screen.findByText(/Сканер журнала: \+1, поиск закономерностей: \+2, ML-задачи: \+1/)).toBeInTheDocument();
  });

  it('approve calls the API; simulate replays the referenced rule', async () => {
    mockedProposals.approve.mockResolvedValue({ ...ruleProposal, status: 'Approved', decisionId: 'deadbeef-0000' });
    mockedReplay.run.mockResolvedValue({ eventsScanned: 120, fires: 5, hits: [], notes: [] });
    render(<Proposals />);
    await screen.findByText('сканер журнала');

    fireEvent.click(screen.getByText('Симуляция'));
    await waitFor(() => expect(mockedReplay.run).toHaveBeenCalledWith(rule, 7));
    expect(await screen.findByText(/Сработало бы 5 раз за 7 дней/)).toBeInTheDocument();

    fireEvent.click(screen.getAllByText('Одобрить')[0]);
    await waitFor(() => expect(mockedProposals.approve).toHaveBeenCalledWith('p1'));
  });

  it('toggles the "how it works" explainer', async () => {
    render(<Proposals />);
    await screen.findByText('сканер журнала');
    expect(screen.queryByText(/Откуда берутся предложения/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Как это работает/ }));
    expect(await screen.findByText(/Откуда берутся предложения/)).toBeInTheDocument();
  });
});
