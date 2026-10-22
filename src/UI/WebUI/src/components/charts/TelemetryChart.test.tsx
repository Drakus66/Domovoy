// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { server } from '../../test/setup';
import TelemetryChart from './TelemetryChart';
import { __resetTelemetryBatch } from './useTelemetryBatch';

const twoBuckets = [
  { timestamp: '2026-07-14T00:00:00Z', value: 20, min: 19, max: 21, avg: 20, count: 3 },
  { timestamp: '2026-07-14T01:00:00Z', value: 22, min: 21, max: 23, avg: 22, count: 3 },
];

interface BatchBody { series: { deviceId: string; capabilityId: string }[]; }

describe('TelemetryChart on the batch loader', () => {
  beforeEach(() => __resetTelemetryBatch());

  it('renders a chart svg once the device series loads', async () => {
    server.use(http.post('*/api/telemetry/aggregate/batch', async ({ request }) => {
      const body = (await request.json()) as BatchBody;
      return HttpResponse.json(body.series.map((s) => ({ ...s, buckets: twoBuckets })));
    }));

    const { container } = render(
      <TelemetryChart capabilityId="temperature" deviceId="d1" hours={24} bucket="hour" unit="°C" />,
    );

    // recharts leaves the chart wrapper in the DOM even at jsdom's zero measured width; reaching it means
    // the component left the loading skeleton and did not fall into the empty / error branch.
    await waitFor(() => expect(container.querySelector('.recharts-responsive-container')).toBeInTheDocument());
    expect(container.querySelector('.MuiSkeleton-root')).not.toBeInTheDocument();
    expect(screen.queryByText(/Нет проб/)).not.toBeInTheDocument();
  });

  it('shows the no-samples note for an empty series', async () => {
    server.use(http.post('*/api/telemetry/aggregate/batch', async ({ request }) => {
      const body = (await request.json()) as BatchBody;
      return HttpResponse.json(body.series.map((s) => ({ ...s, buckets: [] })));
    }));

    render(<TelemetryChart capabilityId="temperature" deviceId="d1" hours={24} bucket="hour" />);

    expect(await screen.findByText(/Нет проб/)).toBeInTheDocument();
  });
});
