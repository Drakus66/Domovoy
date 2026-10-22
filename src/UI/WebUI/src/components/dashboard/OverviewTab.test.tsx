// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import { screen } from '@testing-library/react';
import { render } from '../../test/utils';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import OverviewTab from './OverviewTab';

const device = (overrides: Partial<CapabilityDevice>): CapabilityDevice => ({
  id: Math.random().toString(36).slice(2),
  name: 'Device',
  adapterSource: 'Zigbee2Mqtt',
  zoneId: 'z1',
  capabilities: [],
  state: {},
  isOnline: true,
  lastUpdated: '2026-07-19T10:00:00Z',
  ...overrides,
});

const zoneName = (zoneId?: string | null) => (zoneId === 'z1' ? 'Гостиная' : 'Без зоны');
const noop = () => undefined;

describe('OverviewTab (default home tab)', () => {
  it('groups devices by zone with a climate/light digest in the header', () => {
    render(
      <OverviewTab
        devices={[
          device({ name: 'Датчик', state: { temperature: 20 }, capabilities: [{ id: 'temperature', kind: 'Number', writable: false }] }),
          device({ name: 'Термометр', state: { temperature: 22 }, capabilities: [{ id: 'temperature', kind: 'Number', writable: false }] }),
          device({
            name: 'Лампа',
            state: { on_off: true, brightness: 80 },
            capabilities: [
              { id: 'on_off', kind: 'Boolean', writable: true },
              { id: 'brightness', kind: 'Number', writable: true },
            ],
          }),
        ]}
        zoneName={zoneName}
        loading={false}
        onOpen={noop}
        onCommand={noop}
      />,
    );

    expect(screen.getByText('Гостиная')).toBeInTheDocument();
    // Zone digest: average of 20/22 °C plus the lit-lamp count.
    expect(screen.getByText('21 °C · свет: 1')).toBeInTheDocument();
    expect(screen.getByText('Лампа')).toBeInTheDocument();
  });

  it('shows the empty state with a registry link when the house is empty', () => {
    render(
      <OverviewTab devices={[]} zoneName={zoneName} loading={false} onOpen={noop} onCommand={noop} />,
    );

    expect(screen.getByText(/В доме пока пусто/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Открыть реестр устройств' })).toBeInTheDocument();
  });
});
