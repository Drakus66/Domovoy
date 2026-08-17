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

/** The device whose temperature series stands in for the climate card's trend. */
export function pickClimateTrendDevice(devices: CapabilityDevice[]): CapabilityDevice | undefined {
  return devices.find(
    (d) => 'temperature' in (d.state ?? {}) && Number.isFinite(asNum(d.state.temperature)),
  );
}

/** The heaviest current consumer — its power series stands in for the home's energy trend. */
export function pickEnergyTrendDevice(devices: CapabilityDevice[]): CapabilityDevice | undefined {
  let best: CapabilityDevice | undefined;
  let bestW = -Infinity;
  for (const d of devices) {
    if (!('power' in (d.state ?? {}))) continue;
    const w = asNum(d.state.power);
    if (Number.isFinite(w) && w > bestW) { best = d; bestW = w; }
  }
  return best;
}

export interface OpenSecurityItem { device: CapabilityDevice; kind: 'lock' | 'contact' }

/** Unlocked locks and open contacts — the same semantics summarizeHome counts as "open". */
export function openSecurityItems(devices: CapabilityDevice[]): OpenSecurityItem[] {
  const items: OpenSecurityItem[] = [];
  for (const d of devices) {
    const s = d.state ?? {};
    if ('lock' in s && !asBool(s.lock)) items.push({ device: d, kind: 'lock' });
    if ('contact' in s && asBool(s.contact)) items.push({ device: d, kind: 'contact' });
  }
  return items;
}

/** Top current consumers by watts, heaviest first. */
export function topPowerConsumers(
  devices: CapabilityDevice[], n = 4,
): { device: CapabilityDevice; watts: number }[] {
  return devices
    .filter((d) => 'power' in (d.state ?? {}))
    .map((d) => ({ device: d, watts: asNum(d.state.power) }))
    .filter((x) => Number.isFinite(x.watts) && x.watts > 0)
    .sort((a, b) => b.watts - a.watts)
    .slice(0, n);
}
