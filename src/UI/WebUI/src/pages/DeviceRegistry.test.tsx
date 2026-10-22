// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { server } from '../test/setup';
import { render } from '../test/utils';
import type { CapabilityDevice } from '../api/capabilityDevices';
import DeviceRegistry from './DeviceRegistry';

const device = (overrides: Partial<CapabilityDevice>): CapabilityDevice => ({
  id: Math.random().toString(36).slice(2),
  name: 'Device',
  adapterSource: 'Zigbee2Mqtt',
  zoneId: '',
  capabilities: [],
  state: {},
  isOnline: true,
  lastUpdated: '2026-07-19T10:00:00Z',
  ...overrides,
});

const lamp = device({ id: 'lamp', name: 'Лампа', isOnline: true });
const stale = device({ id: 'stale', name: 'Старый датчик', isOnline: false });
const sun = device({ id: 'sun', name: 'Солнце', adapterSource: 'System', autoArchetype: 'sun' });
const block = device({ id: 'blk', name: 'Гистерезис', adapterSource: 'ControlBlock', autoArchetype: 'control_block' });

function seed(opts: { onDelete?: (id: string) => void } = {}) {
  const deleted = new Set<string>();
  server.use(
    http.get('*/api/capability-devices', () =>
      HttpResponse.json([lamp, stale, sun, block].filter((d) => !deleted.has(d.id)))),
    http.get('*/api/zones', () => HttpResponse.json([])),
    http.delete('*/api/capability-devices/:id', ({ params }) => {
      const id = String(params.id);
      deleted.add(id);
      opts.onDelete?.(id);
      return new HttpResponse(null, { status: 204 });
    }),
    // The detail drawer's lazy telemetry/provenance batches (harmless if never fired).
    http.post('*/api/telemetry/aggregate/batch', () => HttpResponse.json([])),
    http.post('*/api/events/latest-by-device', () => HttpResponse.json([])),
  );
}

describe('DeviceRegistry (/devices)', () => {
  beforeEach(() => seed());

  it('lists user devices and hides service devices by default', async () => {
    render(<DeviceRegistry />);

    expect(await screen.findByText('Лампа')).toBeInTheDocument();
    expect(screen.getByText('Старый датчик')).toBeInTheDocument();
    expect(screen.queryByText('Солнце')).not.toBeInTheDocument();
    expect(screen.queryByText('Гистерезис')).not.toBeInTheDocument();
    // Counter reads visible-of-total.
    expect(screen.getByText('2 из 4')).toBeInTheDocument();
  });

  it('reveals service devices behind the toggle', async () => {
    render(<DeviceRegistry />);
    await screen.findByText('Лампа');

    await userEvent.click(screen.getByText('Служебные'));

    expect(await screen.findByText('Солнце')).toBeInTheDocument();
    expect(screen.getByText('Гистерезис')).toBeInTheDocument();
    expect(screen.getByText('4 из 4')).toBeInTheDocument();
  });

  it('offers delete only for offline devices', async () => {
    render(<DeviceRegistry />);
    await screen.findByText('Лампа');

    expect(screen.getByRole('button', { name: 'Удалить Старый датчик' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Удалить Лампа' })).not.toBeInTheDocument();
  });

  it('deletes an offline device after confirmation and refreshes the list', async () => {
    let deletedId: string | null = null;
    seed({ onDelete: (id) => { deletedId = id; } });
    render(<DeviceRegistry />);
    await screen.findByText('Старый датчик');

    await userEvent.click(screen.getByRole('button', { name: 'Удалить Старый датчик' }));
    // Confirm dialog explains re-discovery; the red button commits.
    await screen.findByText('Удалить устройство?');
    await userEvent.click(screen.getByRole('button', { name: 'Удалить' }));

    await waitFor(() => expect(deletedId).toBe('stale'));
    await waitFor(() => expect(screen.queryByText('Старый датчик')).not.toBeInTheDocument());
    expect(screen.getByText('Лампа')).toBeInTheDocument();
  });
});
