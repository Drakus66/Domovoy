// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { BlockCatalogEntry, ControlBlock } from '../../api/blocks';
import { CapabilityDevice } from '../../api/capabilityDevices';

/**
 * Applicability joins for the ML hub (Epic 2P, decision Р9): a governor type declares which ML target it
 * consumes (`mlTargetCapability`), which writable capability it commands (its first output) and which signal
 * it monitors (its first input). Everything the UI needs — "task → consumer blocks", "task → devices it can
 * govern", "device → applicable models" — is a pure client-side join over the block catalog and the device
 * read-model; no extra backend round-trips.
 */

const eq = (a?: string | null, b?: string | null): boolean =>
  (a ?? '').toLowerCase() === (b ?? '').toLowerCase();

/** Governor block types consuming models of `target`. */
export const governorTypesFor = (catalog: BlockCatalogEntry[], target: string): BlockCatalogEntry[] =>
  catalog.filter((c) => eq(c.mlTargetCapability, target));

/** All governor block types (any ML target). */
export const governorTypes = (catalog: BlockCatalogEntry[]): BlockCatalogEntry[] =>
  catalog.filter((c) => !!c.mlTargetCapability);

/** Block instances of any of `types` — the consumers an ML task's models feed. */
export const consumerBlocksFor = (blocks: ControlBlock[], types: BlockCatalogEntry[]): ControlBlock[] => {
  const ids = new Set(types.map((t) => t.typeId.toLowerCase()));
  return blocks.filter((b) => ids.has(b.typeId.toLowerCase()));
};

/** The writable capability a governor type commands — its bound output (first catalog output). */
export const boundOutputOf = (type: BlockCatalogEntry): string | undefined =>
  type.outputs.find((o) => o.writable)?.id ?? type.outputs[0]?.id;

/** The measured input port name (the signal the governor monitors = the ML target). */
export const measuredInputOf = (type: BlockCatalogEntry): string | undefined => type.inputs[0]?.name;

/**
 * Devices a governor type can govern: those exposing its bound output as a writable capability. Virtual
 * devices of deterministic blocks qualify too — a thermostat loop is exactly what an ML thermostat commands.
 */
export const suitableDevicesFor = (type: BlockCatalogEntry, devices: CapabilityDevice[]): CapabilityDevice[] => {
  const out = boundOutputOf(type);
  if (!out) return [];
  return devices.filter((d) => d.capabilities.some((c) => eq(c.id, out) && c.writable));
};

/** Governor types applicable to a device (Epic 2P, drawer entry): the device exposes the type's writable output. */
export const applicableTypesFor = (catalog: BlockCatalogEntry[], device: CapabilityDevice): BlockCatalogEntry[] =>
  governorTypes(catalog).filter((type) => {
    const out = boundOutputOf(type);
    return !!out && device.capabilities.some((c) => eq(c.id, out) && c.writable);
  });

/**
 * Candidate sources of the measured signal for the drift monitor, best first: the governed device itself if
 * it exposes the capability, then devices in the same zone, then the rest of the house.
 */
export const measuredSourceCandidates = (
  type: BlockCatalogEntry, devices: CapabilityDevice[], governed?: CapabilityDevice | null,
): CapabilityDevice[] => {
  const cap = measuredInputOf(type);
  if (!cap) return [];
  const withCap = devices.filter((d) => d.capabilities.some((c) => eq(c.id, cap)));
  const rank = (d: CapabilityDevice): number =>
    governed && d.id === governed.id ? 0 : governed && d.zoneId && d.zoneId === governed.zoneId ? 1 : 2;
  return [...withCap].sort((a, b) => rank(a) - rank(b));
};
