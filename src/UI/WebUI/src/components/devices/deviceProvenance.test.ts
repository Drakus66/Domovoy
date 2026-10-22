// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { describe, it, expect } from 'vitest';
import { describeProvenance } from './deviceProvenance';
import type { LatestEvent } from '../../api/history';

const ev = (over: Partial<LatestEvent>): LatestEvent => ({
  deviceId: 'd1', timestamp: '2026-07-14T10:00:00Z', capabilityId: 'on_off',
  triggerSource: 'device', ...over,
});

describe('describeProvenance (last-changed-by chip)', () => {
  it('maps each trigger source to its author and accents automation', () => {
    expect(describeProvenance(ev({ triggerSource: 'user' }))).toMatchObject({ author: 'user', accented: false });
    expect(describeProvenance(ev({ triggerSource: 'device' }))).toMatchObject({ author: 'device', accented: false });
    expect(describeProvenance(ev({ triggerSource: 'ml' }))).toMatchObject({ author: 'ml', accented: true });
    expect(describeProvenance(ev({ triggerSource: 'rule' }))).toMatchObject({ author: 'rule', accented: true });
    expect(describeProvenance(ev({ triggerSource: 'block' }))).toMatchObject({ author: 'block', accented: true });
  });

  it('falls back to device for an unknown source', () => {
    expect(describeProvenance(ev({ triggerSource: 'wat' })).author).toBe('device');
  });

  it('resolves the rule/block name via nameOf when available', () => {
    const nameOf = (id?: string | null) => (id === 'rule-1' ? 'Вечер' : null);
    const named = describeProvenance(ev({ triggerSource: 'rule', triggerId: 'rule-1' }), nameOf);
    expect(named.label).toContain('Вечер');

    const generic = describeProvenance(ev({ triggerSource: 'rule', triggerId: 'rule-x' }), nameOf);
    expect(generic.label).not.toContain('Вечер');
    expect(generic.label.length).toBeGreaterThan(0);
  });

  it('carries the change timestamp through as `when`', () => {
    expect(describeProvenance(ev({ timestamp: '2026-07-14T09:00:00Z' })).when).toBe('2026-07-14T09:00:00Z');
  });
});
