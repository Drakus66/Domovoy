// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, waitFor } from '@testing-library/react';
import { render, screen } from '../../test/utils';
import SystemControlCard from './SystemControlCard';
import { systemApi } from '../../api/system';

vi.mock('../../api/system', () => ({
  systemApi: {
    getServices: vi.fn(),
    restartAll: vi.fn(() => Promise.resolve()),
    restartService: vi.fn(() => Promise.resolve()),
    containerAction: vi.fn(() => Promise.resolve()),
  },
}));

const mocked = vi.mocked(systemApi);

describe('SystemControlCard', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    mocked.getServices.mockResolvedValue({
      dockerEnabled: false,
      services: [
        { name: 'db-gateway', kind: 'gateway', selfRestart: true },
        { name: 'connectivity-service', kind: 'service', selfRestart: true },
      ],
    });
  });

  it('lists the services returned by the API', async () => {
    render(<SystemControlCard />);
    expect(await screen.findByText('db-gateway')).toBeInTheDocument();
    expect(screen.getByText('connectivity-service')).toBeInTheDocument();
  });

  it('restarts a single service (self-restart) after confirmation', async () => {
    render(<SystemControlCard />);
    const row = (await screen.findByText('db-gateway')).closest('div')!.parentElement!;
    fireEvent.click(row.querySelector('button')!);
    await waitFor(() => expect(mocked.restartService).toHaveBeenCalledWith('db-gateway', false));
  });

  it('restarts all services from the header button', async () => {
    render(<SystemControlCard />);
    await screen.findByText('db-gateway');
    fireEvent.click(screen.getByRole('button', { name: /restart all|перезапустить всё/i }));
    await waitFor(() => expect(mocked.restartAll).toHaveBeenCalledTimes(1));
  });

  it('uses the Docker path for a container-only unit', async () => {
    mocked.getServices.mockResolvedValue({
      dockerEnabled: true,
      services: [{ name: 'rabbitmq', kind: 'container', selfRestart: false, state: 'running' }],
    });
    render(<SystemControlCard />);
    const row = (await screen.findByText('rabbitmq')).closest('div')!.parentElement!;
    fireEvent.click(row.querySelector('button')!);
    await waitFor(() => expect(mocked.restartService).toHaveBeenCalledWith('rabbitmq', true));
  });
});
