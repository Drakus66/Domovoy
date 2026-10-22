// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect, vi } from 'vitest';
import { fireEvent } from '@testing-library/react';
import { render, screen } from '../../test/utils';
import { TriggerEditor, ConditionEditor, ActionEditor } from './RuleEditors';
import { parseValue } from './ruleValues';
import { CapabilityDevice } from '../../api/capabilityDevices';
import { Zone } from '../../api/zones';
import { RuleAction } from '../../api/automations';

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

  it('renders wait-match + timeout fields for a WaitForEvent action (Epic 3E)', () => {
    render(<ActionEditor devices={devices} caps={caps}
      value={{ type: 'WaitForEvent', waitDeviceId: 'd1', waitCapabilityId: 'motion', waitOperator: 'eq', waitValue: true, timeoutSeconds: 300 }}
      onChange={() => {}} />);
    expect(screen.getByText('Ждать событие')).toBeInTheDocument();
    // the timeout field shows the configured value
    expect(screen.getByDisplayValue('300')).toBeInTheDocument();
  });

  it('writes wait fields (not trigger fields) on change for a WaitForEvent action', () => {
    const onChange = vi.fn();
    render(<ActionEditor devices={devices} caps={caps}
      value={{ type: 'WaitForEvent', waitDeviceId: 'd1', waitCapabilityId: 'motion', waitOperator: 'eq', waitValue: true, timeoutSeconds: 60 }}
      onChange={onChange} />);
    fireEvent.change(screen.getByLabelText('Таймаут, секунды (по умолчанию 300)'), { target: { value: '120' } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ timeoutSeconds: 120 }));
  });

  it('shows the on-error branch toggle for any action type and starts open when a branch exists', () => {
    const onError: RuleAction[] = [{ type: 'Notify', message: 'boom {error}' }];
    render(<ActionEditor devices={devices} caps={caps}
      value={{ type: 'Command', deviceId: 'd1', set: { on_off: true }, onError }} onChange={() => {}} />);
    // the on-error branch's Notify message field is visible because the branch is non-empty (starts expanded)
    expect(screen.getByDisplayValue('boom {error}')).toBeInTheDocument();
  });

  it('does not render branch UI when nestable=false (bounds nesting to one level)', () => {
    // A WaitForEvent rendered inside a branch (nestable=false) must not show its own on-timeout section.
    render(<ActionEditor devices={devices} caps={caps} nestable={false}
      value={{ type: 'WaitForEvent', waitDeviceId: 'd1', waitCapabilityId: 'motion', timeoutSeconds: 5 }}
      onChange={() => {}} />);
    expect(screen.queryByText('При таймауте')).not.toBeInTheDocument();
    expect(screen.queryByText('При ошибке')).not.toBeInTheDocument();
  });
});

// Epic 3G: the context-aware target picker (device/zone scope, coverage, human-readable preview).
const zonedDevices: CapabilityDevice[] = [
  {
    id: 'd1', name: 'Датчик спальни', adapterSource: 'test', zoneId: 'z1', state: {}, isOnline: true, lastUpdated: '',
    capabilities: [{ id: 'temperature', kind: 'Number', writable: false }],
  },
  {
    id: 'd2', name: 'Термостат', adapterSource: 'test', zoneId: 'z1', state: {}, isOnline: true, lastUpdated: '',
    capabilities: [{ id: 'temperature', kind: 'Number', writable: false }],
  },
];
const zonesFixture: Zone[] = [{ id: 'z1', name: 'Спальня', order: 0, createdAt: '', updatedAt: '' }];

describe('TargetPicker context (Epic 3G)', () => {
  it('shows zone-scope coverage ("affects N devices") and a human-readable preview', () => {
    render(<TriggerEditor devices={zonedDevices} zones={zonesFixture}
      value={{ type: 'DeviceState', zoneId: 'z1', capabilityId: 'temperature', operator: 'lt', value: 18 }}
      onChange={() => {}} />);
    // both bedroom devices expose temperature → coverage is 2
    expect(screen.getByText('Затронет 2 устройства в зоне')).toBeInTheDocument();
    expect(screen.getByText('в «Спальня» температура ниже 18')).toBeInTheDocument();
  });

  it('shows the selected device\'s zone as context and a readable preview', () => {
    render(<TriggerEditor devices={zonedDevices} zones={zonesFixture}
      value={{ type: 'DeviceState', deviceId: 'd1', capabilityId: 'temperature', operator: 'lt', value: 18 }}
      onChange={() => {}} />);
    expect(screen.getByText('Зона: Спальня')).toBeInTheDocument();
    expect(screen.getByText('температура у «Датчик спальни» ниже 18')).toBeInTheDocument();
  });

  it('offers a Device/Zone scope toggle only when the home has zones', () => {
    const { rerender } = render(<TriggerEditor devices={zonedDevices} zones={zonesFixture}
      value={{ type: 'DeviceState', deviceId: 'd1', capabilityId: 'temperature', operator: 'lt', value: 18 }}
      onChange={() => {}} />);
    expect(screen.getByRole('button', { name: 'Зона' })).toBeInTheDocument();
    // No zones passed ⇒ device-only, no scope toggle.
    rerender(<TriggerEditor devices={zonedDevices}
      value={{ type: 'DeviceState', deviceId: 'd1', capabilityId: 'temperature', operator: 'lt', value: 18 }}
      onChange={() => {}} />);
    expect(screen.queryByRole('button', { name: 'Зона' })).not.toBeInTheDocument();
  });
});
