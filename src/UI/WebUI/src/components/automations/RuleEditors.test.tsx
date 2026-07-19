// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi } from 'vitest';
import { fireEvent } from '@testing-library/react';
import { render, screen } from '../../test/utils';
import { TriggerEditor, ConditionEditor, ActionEditor } from './RuleEditors';
import { parseValue } from './ruleValues';
import { CapabilityDevice } from '../../api/capabilityDevices';

const devices: CapabilityDevice[] = [{
  id: 'd1', name: 'Датчик', adapterSource: 'test', zoneId: '', state: {}, isOnline: true, lastUpdated: '',
  capabilities: [
    { id: 'motion', kind: 'Boolean', writable: false },
    { id: 'on_off', kind: 'Boolean', writable: true },
  ],
}];
const caps = (id?: string | null) => devices.find((d) => d.id === id)?.capabilities ?? [];

describe('parseValue', () => {
  it('coerces text to typed values (bool / number / string)', () => {
    expect(parseValue('true')).toBe(true);
    expect(parseValue('false')).toBe(false);
    expect(parseValue('')).toBe(true);
    expect(parseValue('42')).toBe(42);
    expect(parseValue('warm')).toBe('warm');
  });
});

describe('TriggerEditor', () => {
  it('shows a localized trigger type (not the bare "DeviceState" code)', () => {
    render(<TriggerEditor devices={devices} caps={caps}
      value={{ type: 'DeviceState', operator: 'eq', value: true }} onChange={() => {}} />);
    expect(screen.getByText('Состояние устройства')).toBeInTheDocument();
  });

  it('parses the value field into a typed value on change', () => {
    const onChange = vi.fn();
    render(<TriggerEditor devices={devices} caps={caps}
      value={{ type: 'DeviceState', deviceId: 'd1', capabilityId: 'motion', operator: 'gt', value: 0 }}
      onChange={onChange} />);
    fireEvent.change(screen.getByLabelText('Значение'), { target: { value: '42' } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ value: 42 }));
  });
});

describe('ConditionEditor', () => {
  it('renders localized home-mode labels for a Mode condition', () => {
    render(<ConditionEditor devices={devices} caps={caps}
      value={{ type: 'Mode', mode: 'Night' }} onChange={() => {}} />);
    expect(screen.getByText('Ночь')).toBeInTheDocument();
  });
});

describe('ActionEditor', () => {
  it('shows the message field for a Notify action', () => {
    render(<ActionEditor devices={devices} caps={caps}
      value={{ type: 'Notify', message: 'Привет' }} onChange={() => {}} />);
    expect(screen.getByText('Уведомление')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Привет')).toBeInTheDocument();
  });

  it('only offers writable capabilities for a Command action', () => {
    const onChange = vi.fn();
    render(<ActionEditor devices={devices} caps={caps}
      value={{ type: 'Command', deviceId: 'd1', set: {} }} onChange={onChange} />);
    // Command type label is localized.
    expect(screen.getByText('Команда устройству')).toBeInTheDocument();
  });

  it('offers the available scenes for a Scene action', () => {
    const onChange = vi.fn();
    render(<ActionEditor devices={devices} caps={caps}
      scenes={[{ id: 's1', name: 'Вечер', targets: [], createdAt: '', updatedAt: '' }]}
      value={{ type: 'Scene', sceneId: 's1' }} onChange={onChange} />);
    expect(screen.getByText('Включить сцену')).toBeInTheDocument();
    expect(screen.getByText('Вечер')).toBeInTheDocument();
  });
});
