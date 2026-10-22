// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import i18n from 'i18next';
import type { CapabilityDevice } from '../../api/capabilityDevices';
import { effectiveArchetype } from '../../api/capabilityDevices';

/**
 * Friendly device naming (roadmap Epic 3G — "Пользовательское имя").
 *
 * A paired Zigbee device announces itself as its IEEE address ("0xa4c1383e04dbda65"), which is
 * unreadable. This module resolves the name the UI should show: the user's alias if set, else a
 * type-derived label when the raw name is machine-assigned, else the raw name (native/ESPHome
 * adapters already report human names). It also builds/strips the optional " в <Zone>" pointer used
 * by the rename-on-zone flow. Pure + i18n-only (no React) so it is trivially testable and reusable.
 */

const dt = (key: string, options?: Record<string, unknown>): string =>
  i18n.t(`devices:${key}`, options ?? {}) as string;

/**
 * True when the adapter-reported name is a machine id we should replace with a type label: a Zigbee
 * IEEE address (0x…hex), a bare GUID, or blank. Human names from native/ESPHome/emulator pass through.
 */
export function isCrypticName(name?: string | null): boolean {
  const n = (name ?? '').trim();
  if (!n) return true;
  if (/^0x[0-9a-f]+$/i.test(n)) return true;
  if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(n)) return true;
  return false;
}

/**
 * A localized default name derived from the device's semantic type (Epic 2D archetype), e.g.
 * "Датчик движения" / "Motion sensor". Falls back to the raw name (or a generic "device") when the
 * archetype is unknown or has no label.
 */
export function autoDeviceName(device: CapabilityDevice): string {
  const arch = effectiveArchetype(device);
  const label = arch && arch !== 'unknown' ? dt(`autoName.${arch}`, { defaultValue: '' }) : '';
  if (label) return label;
  const raw = (device.name ?? '').trim();
  return raw || dt('autoName.device');
}

/**
 * The name the UI should display for a device: the user's alias, else a type label when the raw name
 * is cryptic, else the raw adapter name.
 */
export function deviceLabel(device: CapabilityDevice): string {
  const alias = (device.alias ?? '').trim();
  if (alias) return alias;
  if (isCrypticName(device.name)) return autoDeviceName(device);
  return (device.name ?? '').trim() || autoDeviceName(device);
}

/** The localized connector word for the zone pointer ("в" / "in"). */
const zoneConnector = (): string => dt('autoName.zoneConnector');

/** Append a zone pointer to a base name, e.g. ("Люстра", "Гостиная") → "Люстра в Гостиная". */
export function withZonePointer(base: string, zoneName: string): string {
  return dt('autoName.inZone', { name: base.trim(), zone: zoneName.trim() });
}

/**
 * Remove a trailing " <connector> <zoneName>" pointer for any of the given zone names, so the base
 * name can be re-pointed at a new zone. Names without a recognized pointer are returned trimmed.
 */
export function stripZonePointer(name: string, zoneNames: string[]): string {
  const c = zoneConnector();
  const trimmed = name.trim();
  for (const z of zoneNames) {
    const zn = z.trim();
    if (!zn) continue;
    const suffix = ` ${c} ${zn}`;
    if (trimmed.toLocaleLowerCase().endsWith(suffix.toLocaleLowerCase())) {
      return trimmed.slice(0, trimmed.length - suffix.length).trim();
    }
  }
  return trimmed;
}

/**
 * Propose a new name for a device whose zone is changing: strip any existing zone pointer, then append
 * the new zone's pointer (or nothing when unassigning). `zoneNames` is every known zone name so an
 * old pointer can be recognized regardless of which zone it referenced.
 */
export function proposeZoneName(
  currentLabel: string,
  newZoneName: string | null,
  zoneNames: string[],
): string {
  const base = stripZonePointer(currentLabel, zoneNames);
  return newZoneName ? withZonePointer(base, newZoneName) : base;
}

/** Whether a device's current label already carries a pointer to the given zone name. */
export function hasZonePointer(currentLabel: string, zoneName: string): boolean {
  const c = zoneConnector();
  return currentLabel.trim().toLocaleLowerCase().endsWith(` ${c} ${zoneName.trim()}`.toLocaleLowerCase());
}
