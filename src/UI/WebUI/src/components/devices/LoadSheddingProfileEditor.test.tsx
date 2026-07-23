// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '../../test/utils';
import LoadSheddingProfileEditor from './LoadSheddingProfileEditor';
import { loadManagementApi } from '../../api/loadManagement';
import type { CapabilityDevice } from '../../api/capabilityDevices';

vi.mock('../../api/loadManagement', () => ({
  loadManagementApi: {
    setDeviceProfile: vi.fn().mockResolvedValue(undefined),
  },
}));

const mocked = vi.mocked(loadManagementApi);

const baseDevice: CapabilityDevice = {
  id: 'heater-1',
  name: 'Water heater',
  adapterSource: 'Zigbee2Mqtt',
  zoneId: 'z',
  capabilities: [{ id: 'on_off', kind: 'Boolean', writable: true }],
  state: {},
  isOnline: true,
  lastUpdated: '',
};

describe('LoadSheddingProfileEditor', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('renders nothing for a device without a writable on_off capability', () => {
    const device: CapabilityDevice = { ...baseDevice, capabilities: [{ id: 'temperature', kind: 'Number', writable: false }] };
    const { container } = render(<LoadSheddingProfileEditor device={device} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows only the enable switch until enabled', () => {
    render(<LoadSheddingProfileEditor device={baseDevice} />);
    expect(screen.getByText('Управление нагрузкой')).toBeInTheDocument();
    expect(screen.queryByText('Защищённое устройство')).not.toBeInTheDocument();
  });

  it('reveals the profile fields once enabled and saves the profile', async () => {
    render(<LoadSheddingProfileEditor device={baseDevice} />);

    fireEvent.click(screen.getByRole('checkbox', { name: /Управлять этой нагрузкой/i }));
    expect(await screen.findByText('Защищённое устройство')).toBeInTheDocument();
    // The watts moved to the energy profile (Epic 3C-D) — this editor only points at them.
    expect(screen.getByText(/Мощность берётся из блока/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Сохранить профиль/i }));

    await waitFor(() =>
      expect(mocked.setDeviceProfile).toHaveBeenCalledWith(
        'heater-1',
        expect.objectContaining({ enabled: true, controlCapabilityId: 'on_off' }),
      ),
    );
  });

  it('disables the curtailable switch when the device has no writable numeric capability', () => {
    render(<LoadSheddingProfileEditor device={baseDevice} />);
    fireEvent.click(screen.getByRole('checkbox', { name: /Управлять этой нагрузкой/i }));
    expect(screen.getByRole('checkbox', { name: /Можно снижать/i })).toBeDisabled();
  });
});
