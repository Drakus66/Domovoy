// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../../test/setup';
import DeviceTile from './DeviceTile';
import { __resetProvenance } from './useDeviceProvenance';
import { __resetTelemetryBatch } from '../charts/useTelemetryBatch';
import type { CapabilityDevice } from '../../api/capabilityDevices';

const twoBuckets = [
  { timestamp: '2026-07-14T00:00:00Z', value: 20, min: 20, max: 20, avg: 20, count: 1 },
  { timestamp: '2026-07-14T01:00:00Z', value: 22, min: 22, max: 22, avg: 22, count: 1 },
];

const tempDevice: CapabilityDevice = {
  id: 'dev-temp', name: 'Гостиная — климат', adapterSource: 'test', zoneId: '',
  capabilities: [{ id: 'temperature', kind: 'Number', writable: false }],
  state: { temperature: 21.4 }, isOnline: true, lastUpdated: '',
};

const switchDevice: CapabilityDevice = {
  id: 'dev-switch', name: 'Розетка', adapterSource: 'test', zoneId: '',
  capabilities: [{ id: 'on_off', kind: 'Boolean', writable: true }],
  state: { on_off: false }, isOnline: true, lastUpdated: '',
};

interface BatchBody { series: { deviceId: string; capabilityId: string }[]; }

function seed(latest: unknown[]) {
  server.use(
    http.post('*/api/telemetry/aggregate/batch', async ({ request }) => {
      const body = (await request.json()) as BatchBody;
      return HttpResponse.json(body.series.map((s) => ({ ...s, buckets: twoBuckets })));
    }),
    http.post('*/api/events/latest-by-device', () => HttpResponse.json(latest)),
  );
}

describe('DeviceTile enrichment (block C)', () => {
  beforeEach(() => { __resetProvenance(); __resetTelemetryBatch(); });

  it('shows a "last changed by" chip resolved from the latest event', async () => {
    seed([{
      deviceId: 'dev-temp', timestamp: new Date(Date.now() - 5 * 60_000).toISOString(),
      capabilityId: 'temperature', triggerSource: 'rule', triggerId: 'r1', ruleId: 'r1',
    }]);

    render(<DeviceTile device={tempDevice} onOpen={() => {}} onCommand={() => {}} />);

    expect(await screen.findByText('Правило')).toBeInTheDocument();
  });

  it('omits the chip when the device has no recorded history', async () => {
    seed([]); // latest-by-device returns nothing for this id
    render(<DeviceTile device={switchDevice} onOpen={() => {}} onCommand={() => {}} />);

    // The switch renders (its name is present) but no provenance chip appears.
    expect(await screen.findByText('Розетка')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText('Правило')).not.toBeInTheDocument());
    expect(screen.queryByText('Вы')).not.toBeInTheDocument();
  });
});
