// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { http, HttpResponse } from 'msw';
import { server } from '../../test/setup';
import DomovoyRail from './DomovoyRail';

const activity = [
  { timestamp: '2026-07-14T08:30:00Z', source: 'automation', severity: 'info', title: 'Свет выключен по расписанию' },
];

const proposal = {
  id: 'p1', kind: 'MlTask', status: 'Proposed', title: 'Обучить целевую температуру',
  source: 'ml_task_scanner', decisionId: 'dec-123', createdAt: '2026-07-14T08:00:00Z',
};

function seed(opts: { proposals?: unknown[]; onApprove?: () => void } = {}) {
  server.use(
    http.get('*/api/activity', () => HttpResponse.json(activity)),
    http.get('*/api/automations', () => HttpResponse.json([])),
    http.get('*/api/proposals', () => HttpResponse.json(opts.proposals ?? [proposal])),
    http.post('*/api/proposals/:id/approve', () => {
      opts.onApprove?.();
      return HttpResponse.json({ ...proposal, status: 'Approved' });
    }),
  );
}

const renderRail = () =>
  render(<MemoryRouter><DomovoyRail devices={[]} /></MemoryRouter>);

describe('DomovoyRail (dashboard rail)', () => {
  beforeEach(() => seed());

  it('shows today activity and the pending-proposal count', async () => {
    renderRail();
    expect(await screen.findByText('Свет выключен по расписанию')).toBeInTheDocument();
    // "Ждёт решения · 1"
    expect(await screen.findByText(/Ждёт решения · 1/)).toBeInTheDocument();
    expect(screen.getByText('Обучить целевую температуру')).toBeInTheDocument();
  });

  it('approves a proposal via the API and drops it from the queue', async () => {
    let approved = false;
    seed({ onApprove: () => { approved = true; } });
    renderRail();

    const approveBtn = await screen.findByRole('button', { name: 'Принять' });
    await userEvent.click(approveBtn);

    await waitFor(() => expect(approved).toBe(true));
    await waitFor(() => expect(screen.queryByText('Обучить целевую температуру')).not.toBeInTheDocument());
  });

  it('defers a proposal locally without calling the server', async () => {
    let approveCalled = false;
    seed({ onApprove: () => { approveCalled = true; } });
    renderRail();

    const laterBtn = await screen.findByRole('button', { name: 'Позже' });
    await userEvent.click(laterBtn);

    // Panel hides once the only proposal is dismissed; no approve/reject request went out.
    await waitFor(() => expect(screen.queryByText('Обучить целевую температуру')).not.toBeInTheDocument());
    expect(approveCalled).toBe(false);
  });

  it('hides the queue panel when nothing is pending', async () => {
    seed({ proposals: [] });
    renderRail();
    expect(await screen.findByText('Свет выключен по расписанию')).toBeInTheDocument();
    expect(screen.queryByText(/Ждёт решения/)).not.toBeInTheDocument();
  });
});
