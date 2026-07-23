// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi } from 'vitest';
import { fireEvent } from '@testing-library/react';
import { render, screen } from '../../test/utils';
import RequiredExpressionEditor from './RequiredExpressionEditor';
import { CapabilityDevice } from '../../api/capabilityDevices';

const devices: CapabilityDevice[] = [{
  id: 'd1', name: 'Датчик', adapterSource: 'test', zoneId: '', state: {}, isOnline: true, lastUpdated: '',
  capabilities: [{ id: 'occupancy', kind: 'Boolean', writable: false }],
}];
const caps = (id?: string | null) => devices.find((d) => d.id === id)?.capabilities ?? [];

describe('RequiredExpressionEditor (Epic 3E)', () => {
  it('shows only an "add" button when there is no gate yet', () => {
    const onChange = vi.fn();
    render(<RequiredExpressionEditor value={null} onChange={onChange} devices={devices} caps={caps} />);
    fireEvent.click(screen.getByText('Добавить обязательное условие'));
    expect(onChange).toHaveBeenCalledWith({ conditions: [], expression: '' });
  });

  it('renders indexed condition chips (C0…) and a removal button when a gate exists', () => {
    render(<RequiredExpressionEditor devices={devices} caps={caps} onChange={() => {}}
      value={{ expression: 'C0', conditions: [{ type: 'Mode', mode: 'Home' }] }} />);
    // "C0" appears both as the condition's index chip and as a token-insert chip — both are expected.
    expect(screen.getAllByText('C0').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Убрать обязательное условие')).toBeInTheDocument();
  });

  it('appends a token to the expression when a token chip is clicked', () => {
    const onChange = vi.fn();
    render(<RequiredExpressionEditor devices={devices} caps={caps} onChange={onChange}
      value={{ expression: 'C0', conditions: [{ type: 'Mode', mode: 'Home' }] }} />);
    fireEvent.click(screen.getByText('&&'));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ expression: 'C0 &&' }));
  });

  it('clears the gate when "remove" is clicked', () => {
    const onChange = vi.fn();
    render(<RequiredExpressionEditor devices={devices} caps={caps} onChange={onChange}
      value={{ expression: '', conditions: [] }} />);
    fireEvent.click(screen.getByText('Убрать обязательное условие'));
    expect(onChange).toHaveBeenCalledWith(null);
  });
});
