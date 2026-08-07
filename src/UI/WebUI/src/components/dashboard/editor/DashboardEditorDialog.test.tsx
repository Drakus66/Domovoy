// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '../../../test/utils';
import DashboardEditorDialog from './DashboardEditorDialog';
import { dashboardsApi } from '../../../api/dashboards';
import type { CapabilityDevice } from '../../../api/capabilityDevices';
import { useConfirmStore } from '../../../store/confirmStore';

vi.mock('../../../api/dashboards', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../../api/dashboards')>();
  return {
    ...original,
    dashboardsApi: {
      list: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      remove: vi.fn(),
      reorder: vi.fn(),
      getPrefs: vi.fn(),
      savePrefs: vi.fn(),
    },
  };
});

const mockedApi = vi.mocked(dashboardsApi);

const lamp: CapabilityDevice = {
  id: 'lamp-1',
  name: 'Лампа гостиная',
  adapterSource: 'zigbee',
  zoneId: '',
  capabilities: [{ id: 'on_off', kind: 'Boolean', writable: true }],
  state: { on_off: false },
  isOnline: true,
  lastUpdated: '2026-07-06T00:00:00Z',
};

describe('DashboardEditorDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedApi.create.mockResolvedValue({
      id: 'new-1', name: 'Свет', icon: null, order: 0, sections: [],
      createdAt: '2026-07-06T00:00:00Z', updatedAt: '2026-07-06T00:00:00Z',
    });
  });

  it('creates a tab with a section and a picked device item', async () => {
    render(
      <DashboardEditorDialog open dashboard={null} devices={[lamp]} onClose={() => undefined} />,
    );

    fireEvent.change(screen.getByLabelText('Название'), { target: { value: 'Свет' } });

    fireEvent.click(screen.getByRole('button', { name: 'Добавить секцию' }));
    fireEvent.change(screen.getByPlaceholderText('Название секции'), { target: { value: 'Лампы' } });

    // Two-step picker: item type → device checkboxes.
    fireEvent.click(screen.getByRole('button', { name: 'Добавить элементы' }));
    fireEvent.click(await screen.findByText('Устройство'));
    fireEvent.click(await screen.findByText('Лампа гостиная'));
    fireEvent.click(screen.getByRole('button', { name: 'Добавить' }));

    // The item now shows in the section list; the picker dialog closes (MUI exit
    // transition keeps it aria-hidden briefly — find* waits it out).
    expect(await screen.findByText('Лампа гостиная')).toBeInTheDocument();

    fireEvent.click(await screen.findByRole('button', { name: 'Сохранить' }));
    await waitFor(() =>
      expect(mockedApi.create).toHaveBeenCalledWith({
        name: 'Свет',
        icon: null,
        sections: [{ title: 'Лампы', items: [{ type: 'device', deviceId: 'lamp-1' }] }],
      }),
    );
  });

  it('reorders sections with the move-up control', async () => {
    mockedApi.update.mockResolvedValue(undefined);
    render(
      <DashboardEditorDialog
        open
        dashboard={{
          id: 'dash-1', name: 'Климат', icon: null, order: 0,
          sections: [
            { title: 'Первая', items: [] },
            { title: 'Вторая', items: [] },
          ],
          createdAt: '2026-07-06T00:00:00Z', updatedAt: '2026-07-06T00:00:00Z',
        }}
        devices={[lamp]}
        onClose={() => undefined}
      />,
    );

    // The second section's move-up button is the last enabled "Выше".
    const upButtons = screen.getAllByRole('button', { name: 'Выше' });
    fireEvent.click(upButtons[upButtons.length - 1]);

    fireEvent.click(screen.getByRole('button', { name: 'Сохранить' }));
    await waitFor(() =>
      expect(mockedApi.update).toHaveBeenCalledWith('dash-1', expect.objectContaining({
        sections: [
          { title: 'Вторая', items: [] },
          { title: 'Первая', items: [] },
        ],
      })),
    );
  });

  it('asks for confirmation before deleting', async () => {
    mockedApi.remove.mockResolvedValue(undefined);
    // Подтверждение — общий диалог интерфейса (store/confirmStore), а не window.confirm: он живёт
    // в теме приложения, локализуется и не подавляется браузером в киоске.
    const asked: string[] = [];
    const realAsk = useConfirmStore.getState().ask;
    useConfirmStore.setState({
      ask: (request) => { asked.push(request.message); return Promise.resolve(true); },
    });
    render(
      <DashboardEditorDialog
        open
        dashboard={{
          id: 'dash-1', name: 'Климат', icon: null, order: 0, sections: [],
          createdAt: '2026-07-06T00:00:00Z', updatedAt: '2026-07-06T00:00:00Z',
        }}
        devices={[]}
        onClose={() => undefined}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Удалить вкладку' }));
    await waitFor(() => expect(mockedApi.remove).toHaveBeenCalledWith('dash-1'));
    expect(asked.join(' ')).toContain('Климат');
    useConfirmStore.setState({ ask: realAsk });
  });
});
