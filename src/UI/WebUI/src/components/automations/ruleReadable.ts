// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Epic 3G — human-readable rule authoring. Pure (JSX-free) helpers shared by the rule editors (live
// preview + context-aware target picker) and the Automations page (rule-card summaries). Kept out of the
// editor components so the module exports only functions (Fast Refresh / react-refresh lint rule) and can
// be unit-tested without rendering. Nothing here changes the wire model — a zone-scoped device-state match
// (`zoneId` + capability, no `deviceId`) is already honoured by the backend RuleEvaluator (any device in
// the zone), so this is presentation over an existing capability + archetype model.

import i18n from 'i18next';
import type { Zone } from '../../api/zones';
import type { Capability, CapabilityDevice } from '../../api/capabilityDevices';
import { isUnassignedZone } from '../../api/capabilityDevices';
import { capabilityLabel } from '../devices/deviceVisuals';

/** The device-state selection shared by triggers, conditions and wait-for-event actions. */
export interface DeviceMatch {
  deviceId?: string | null;
  zoneId?: string | null;
  capabilityId?: string | null;
  operator?: string | null;
  value?: unknown;
}

const tr = (key: string, options?: Record<string, unknown>): string => i18n.t(`automations:${key}`, options ?? {});

/** Resolve a zone id to its display name (localized "unassigned" / "unknown" fallbacks). */
export const zoneNameOf = (zones: Zone[], zoneId?: string | null): string => {
  if (isUnassignedZone(zoneId)) return tr('target.unassignedZone');
  return zones.find((z) => z.id === zoneId)?.name ?? tr('target.unknownZone');
};

/** The zone a device is assigned to, rendered for the picker's context line. */
export const deviceZoneName = (zones: Zone[], device?: CapabilityDevice | null): string =>
  zoneNameOf(zones, device?.zoneId);

/** Devices assigned to a zone (empty when no zone is given). */
export const devicesInZone = (devices: CapabilityDevice[], zoneId?: string | null): CapabilityDevice[] =>
  !zoneId ? [] : devices.filter((d) => d.zoneId === zoneId);

/**
 * How many devices a zone-scoped target actually reaches — the "affects N" coverage the HA target-picker
 * shows. With a capability, counts only devices in the zone that expose it (the set the backend would
 * evaluate/command); without one, the whole zone.
 */
export const zoneCoverage = (
  devices: CapabilityDevice[], zoneId?: string | null, capabilityId?: string | null,
): number => {
  const inZone = devicesInZone(devices, zoneId);
  if (!capabilityId) return inZone.length;
  return inZone.filter((d) => d.capabilities.some((c) => c.id === capabilityId)).length;
};

/**
 * The capabilities offered when targeting a whole zone: the union across the zone's devices, deduped by id
 * (first device wins for metadata). `writableOnly` narrows to actuatable capabilities (command targets).
 */
export const capabilitiesInZone = (
  devices: CapabilityDevice[], zoneId?: string | null, opts?: { writableOnly?: boolean },
): Capability[] => {
  const seen = new Map<string, Capability>();
  for (const d of devicesInZone(devices, zoneId)) {
    for (const c of d.capabilities) {
      if (opts?.writableOnly && !c.writable) continue;
      if (!seen.has(c.id)) seen.set(c.id, c);
    }
  }
  return [...seen.values()];
};

/** Zones that hold at least one device, each carrying its device count (for the zone dropdown). */
export const zonesWithDevices = (
  zones: Zone[], devices: CapabilityDevice[],
): Array<{ zone: Zone; count: number }> =>
  zones
    .map((zone) => ({ zone, count: devices.filter((d) => d.zoneId === zone.id).length }))
    .filter((z) => z.count > 0);

/** Compact operator word for a sentence ("ниже" / "выше" / "равно" / "изменилось"), not a math symbol. */
export const operatorWord = (op?: string | null): string =>
  tr(`readable.op.${op || 'eq'}`, { defaultValue: tr('readable.op.eq') });

/** Typed value → sentence fragment (booleans as localized on/off; blank for empty). */
export const readableValue = (v: unknown): string => {
  if (typeof v === 'boolean') return tr(v ? 'readable.on' : 'readable.off');
  if (v === null || v === undefined || v === '') return '';
  return String(v);
};

/**
 * Render a device-state match as a human sentence fragment (no When/If prefix) over the capability +
 * archetype vocabulary — e.g. «в «Спальня» температура ниже 18» (zone-scoped) or «температура у «Датчик»
 * ниже 18» (single device). Returns '' when nothing is selected yet so callers can hide the preview.
 */
export const readableMatch = (devices: CapabilityDevice[], zones: Zone[], m: DeviceMatch): string => {
  const boundToDevice = !!m.deviceId;
  const boundToZone = !boundToDevice && !!m.zoneId;
  if (!boundToDevice && !boundToZone) return '';

  const cap = m.capabilityId ? capabilityLabel(m.capabilityId).toLocaleLowerCase() : tr('trigger.any');
  const changed = (m.operator || '') === 'changed';

  if (boundToZone) {
    const zone = zoneNameOf(zones, m.zoneId);
    return changed
      ? tr('readable.zoneChanged', { zone, capability: cap })
      : tr('readable.zone', { zone, capability: cap, op: operatorWord(m.operator), value: readableValue(m.value) });
  }
  const device = devices.find((d) => d.id === m.deviceId)?.name ?? tr('target.pickDevice');
  return changed
    ? tr('readable.deviceChanged', { device, capability: cap })
    : tr('readable.device', { device, capability: cap, op: operatorWord(m.operator), value: readableValue(m.value) });
};
