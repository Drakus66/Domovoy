// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { MemoryRouter } from 'react-router-dom';
import { server } from '../test/setup';
import Presence from './Presence';

const RESIDENTS = [
  { id: 'r1', displayName: 'Аня', userId: null, ownTracksId: 'anya', trackingEnabled: true },
  { id: 'r2', displayName: 'Борис', userId: null, ownTracksId: null, trackingEnabled: true },
];

function mock(status: { anyoneHome: boolean; homeCount: number; residents: unknown[] }) {
  server.use(
    http.get('*/api/residents', () => HttpResponse.json(RESIDENTS)),
    http.get('*/api/users', () => HttpResponse.json([])),
    http.get('*/api/settings/presence', () =>
      HttpResponse.json({ id: 'current', homeRadiusMeters: 150, awayGraceSeconds: 180, ownTracksToken: null })),
    http.get('*/api/presence/status', () => HttpResponse.json(status)),
  );
}

const renderPage = () => render(<MemoryRouter><Presence /></MemoryRouter>);

describe('Presence page', () => {
  beforeEach(() => {
    mock({
      anyoneHome: true,
      homeCount: 1,
      residents: [
        { id: 'r1', displayName: 'Аня', ownTracksId: 'anya', trackingEnabled: true, home: true, battery: 84, lastReportAt: null },
        { id: 'r2', displayName: 'Борис', ownTracksId: null, trackingEnabled: true, home: false, battery: null, lastReportAt: null },
      ],
    });
  });

  it('lists residents with their live home/away state', async () => {
    renderPage();
    expect(await screen.findByText('Аня')).toBeInTheDocument();
    expect(screen.getByText('Борис')).toBeInTheDocument();
    // Live status from /api/presence/status: Аня home, Борис away.
    await waitFor(() => expect(screen.getByText('Дома')).toBeInTheDocument());
    expect(screen.getByText('Ушёл')).toBeInTheDocument();
    // Battery chip from the status snapshot.
    expect(screen.getByText('84%')).toBeInTheDocument();
  });

  it('shows the occupancy aggregate that feeds the presence_mode block', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('Кто-то дома (1)')).toBeInTheDocument());
  });

  it('renders the geofence settings', async () => {
    renderPage();
    expect(await screen.findByText('Геозона')).toBeInTheDocument();
    expect(screen.getByLabelText('Радиус дома')).toHaveValue(150);
  });
});
