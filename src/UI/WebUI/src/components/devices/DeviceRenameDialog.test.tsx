// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi } from 'vitest';
import { fireEvent, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../../test/setup';
import { render, screen } from '../../test/utils';
import DeviceRenameDialog, { RenameProposal } from './DeviceRenameDialog';
import type { CapabilityDevice } from '../../api/capabilityDevices';

const dev = (id: string): CapabilityDevice => ({
  id, name: '0x' + id, adapterSource: 'Zigbee2Mqtt', zoneId: '',
  capabilities: [], state: {}, isOnline: false, lastUpdated: '',
});

const proposal = (id: string, current: string, proposed: string): RenameProposal =>
  ({ device: dev(id), current, proposed });

describe('DeviceRenameDialog', () => {
  it('renders a table with approve/reject for several devices, and disables confirm when none is approved', () => {
    const proposals = [
      proposal('a', 'Свет', 'Свет в Гостиная'),
      proposal('b', 'Датчик', 'Датчик в Гостиная'),
    ];
    render(
      <DeviceRenameDialog proposals={proposals} open onClose={() => {}} onApplied={() => {}} />,
    );

    expect(screen.getByText('Свет в Гостиная')).toBeInTheDocument();
    expect(screen.getByText('Датчик в Гостиная')).toBeInTheDocument();

    const confirm = screen.getByRole('button', { name: 'Подтвердить' });
    expect(confirm).toBeEnabled();

    fireEvent.click(screen.getByRole('button', { name: 'Отклонить все' }));
    expect(confirm).toBeDisabled();
  });

  it('writes the alias for approved devices on confirm and reports them back', async () => {
    const seen: Record<string, string> = {};
    server.use(
      http.put('*/api/capability-devices/:id/alias', async ({ params, request }) => {
        const body = (await request.json()) as { alias: string | null };
        seen[params.id as string] = body.alias ?? '';
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const onApplied = vi.fn();
    render(
      <DeviceRenameDialog
        proposals={[proposal('a', 'Свет', 'Свет в Гостиная')]}
        open
        onClose={() => {}}
        onApplied={onApplied}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Переименовать' }));

    await waitFor(() => expect(onApplied).toHaveBeenCalledWith({ a: 'Свет в Гостиная' }));
    expect(seen.a).toBe('Свет в Гостиная');
  });
});
