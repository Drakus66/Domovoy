// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import type { CapabilityDevice } from '../../api/capabilityDevices';
import { asBool, asNum } from '../devices/deviceVisuals';

/** Home "pulse" derived from the loaded devices — the numbers behind the state band's four cards. */
export interface HomeSummary {
  climate: { temp?: number; humidity?: number; co2?: number };
  security: { locks: number; openCount: number };
  energy: { watts: number; kwh: number };
}

const avg = (xs: number[]): number | undefined =>
  xs.length ? Math.round((xs.reduce((s, x) => s + x, 0) / xs.length) * 10) / 10 : undefined;

/** Aggregate climate / security / energy from the device list (pure — unit-tested directly). */
export function summarizeHome(devices: CapabilityDevice[]): HomeSummary {
  const has = (d: CapabilityDevice, cap: string) => cap in (d.state ?? {});
  const temps: number[] = []; const hums: number[] = []; const co2s: number[] = [];
  let locks = 0; let openCount = 0; let watts = 0; let kwh = 0;

  for (const d of devices) {
    const s = d.state ?? {};
    if (has(d, 'temperature')) temps.push(asNum(s.temperature));
    if (has(d, 'humidity')) hums.push(asNum(s.humidity));
    if (has(d, 'co2')) co2s.push(asNum(s.co2));
    if (has(d, 'lock')) { locks += 1; if (!asBool(s.lock)) openCount += 1; }
    if (has(d, 'contact') && asBool(s.contact)) openCount += 1;
    if (has(d, 'power')) watts += asNum(s.power);
    if (has(d, 'energy')) kwh += asNum(s.energy);
  }

  return {
    climate: { temp: avg(temps), humidity: avg(hums), co2: avg(co2s) },
    security: { locks, openCount },
    energy: { watts: Math.round(watts), kwh: Math.round(kwh * 10) / 10 },
  };
}
