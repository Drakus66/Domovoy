// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../../test/utils';
import UpdatesEditor from './UpdatesEditor';
import { updatesApi } from '../../api/updates';

vi.mock('../../api/updates', () => ({
  updatesApi: {
    getSettings: vi.fn(),
    saveSettings: vi.fn(),
    components: vi.fn(),
    check: vi.fn(),
    plan: vi.fn(),
    apply: vi.fn(),
    status: vi.fn(),
    history: vi.fn(),
    rollback: vi.fn(),
  },
}));

const mocked = vi.mocked(updatesApi);

const settings = {
  id: 'current',
  channel: 'release' as const,
  checkEnabled: true,
  checkIntervalHours: 6,
  backupBeforeUpdate: true,
  lastCheckAt: null,
  lastCheckResult: null,
  updatedAt: '',
};

const components = [
  { name: 'db-gateway', installed: '1.0.10', digest: 'sha256:a', available: '1.0.10', hasUpdate: false, deps: null },
  { name: 'automation-service', installed: '1.0.10', digest: 'sha256:b', available: '1.0.11', hasUpdate: true, deps: null },
];

/**
 * Кнопка «Обновить» есть в каждой строке таблицы, но у актуальных компонентов она отключена —
 * берём единственную активную, а не первую попавшуюся.
 */
const rowUpdateButton = () =>
  screen.getAllByRole('button', { name: /^Обновить$/i })
    .find((b) => !(b as HTMLButtonElement).disabled) as HTMLElement;

describe('UpdatesEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocked.getSettings.mockResolvedValue(settings);
    mocked.components.mockResolvedValue({ channel: 'release', components });
    mocked.status.mockResolvedValue({ status: 'idle' });
    mocked.history.mockResolvedValue([]);
    mocked.saveSettings.mockResolvedValue(settings);
  });

  it('shows what is installed against what the channel offers', async () => {
    render(<UpdatesEditor />);
    await waitFor(() => expect(mocked.components).toHaveBeenCalled());

    expect(await screen.findByText('automation-service')).toBeInTheDocument();
    // Компонент с обновлением показывает доступную версию, актуальный — «актуально».
    expect(screen.getByText('1.0.11')).toBeInTheDocument();
    expect(screen.getByText(/актуально/i)).toBeInTheDocument();
  });

  it('shows the pulled-in components and the reason before anything is applied', async () => {
    // Главная ценность диалога: «вместе с этим обновится вот это, потому что…» — до нажатия.
    mocked.plan.mockResolvedValue({
      channel: 'release',
      ok: true,
      refusal: null,
      conflict: [],
      empty: false,
      groups: [{
        atomic: false,
        members: [
          {
            component: 'db-gateway', container: 'db-gateway', from: '1.0.10', to: '1.0.12',
            reason: 'automation-service требует db-api ≥ 5',
          },
          {
            component: 'automation-service', container: 'automation-service', from: '1.0.10', to: '1.0.11',
            reason: 'запрошено обновление',
          },
        ],
      }],
    });

    render(<UpdatesEditor />);
    await screen.findByText('automation-service');

    fireEvent.click(rowUpdateButton());

    await waitFor(() => expect(mocked.plan).toHaveBeenCalled());
    expect(await screen.findByText(/automation-service требует db-api/i)).toBeInTheDocument();
    // Ничего не применилось от одного лишь открытия диалога.
    expect(mocked.apply).not.toHaveBeenCalled();
  });

  it('explains an atomic group instead of just listing it', async () => {
    mocked.plan.mockResolvedValue({
      channel: 'release',
      ok: true,
      refusal: null,
      conflict: [],
      empty: false,
      groups: [{
        atomic: true,
        members: [
          { component: 'api-gateway', container: 'api-gateway', from: '1.0', to: '2.0', reason: 'взаимная зависимость' },
          { component: 'automation-service', container: 'automation-service', from: '1.0', to: '2.0', reason: 'взаимная зависимость' },
        ],
      }],
    });

    render(<UpdatesEditor />);
    await screen.findByText('automation-service');
    fireEvent.click(rowUpdateButton());

    expect(await screen.findByText(/обновятся вместе/i)).toBeInTheDocument();
  });

  it('surfaces a refusal with the conflicting pair and blocks applying', async () => {
    mocked.plan.mockResolvedValue({
      channel: 'release',
      ok: false,
      refusal: 'В канале нет совместимой версии db-gateway.',
      conflict: ['automation-service', 'db-gateway'],
      empty: false,
      groups: [],
    });

    render(<UpdatesEditor />);
    await screen.findByText('automation-service');
    fireEvent.click(rowUpdateButton());

    expect(await screen.findByText(/нет совместимой версии/i)).toBeInTheDocument();
    expect(screen.getByText(/automation-service ↔ db-gateway/)).toBeInTheDocument();
  });

  it('switches the channel through the API', async () => {
    render(<UpdatesEditor />);
    await waitFor(() => expect(mocked.getSettings).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('radio', { name: /Разработка/i }));

    await waitFor(() => expect(mocked.saveSettings).toHaveBeenCalledWith({ channel: 'dev' }));
  });

  it('degrades to a plain notice when the delivery service is absent', async () => {
    // Стек, поднятый без службы обновлений, должен объяснять это, а не падать ошибкой.
    mocked.getSettings.mockRejectedValue(new Error('503'));
    mocked.components.mockRejectedValue(new Error('503'));

    render(<UpdatesEditor />);

    expect(await screen.findByText(/Служба обновлений недоступна/i)).toBeInTheDocument();
  });
});
