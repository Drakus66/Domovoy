// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Client-side localization of the block catalog served by AutomationService. The catalog's titles,
// descriptions, port and option descriptions are English source-of-truth strings; the UI overlays a
// translation when the `blocks` namespace has one (`type.<typeId>.*`, `input.<typeId>.<port>`,
// `option.<typeId>.<name>.desc`, `param.<typeId>.<name>.*`) and falls back to the server text — an
// unknown/new type from a plugin or a composite degrades to its catalog strings, never to a bare key.

import i18n from 'i18next';

/** Localized display title of a block type; falls back to the catalog's English title. */
export const blockTypeTitle = (typeId: string, fallback: string): string =>
  i18n.t(`blocks:type.${typeId}.title`, { defaultValue: fallback });

/** Localized description of a block type; falls back to the catalog's English description. */
export const blockTypeDesc = (typeId: string, fallback: string): string =>
  i18n.t(`blocks:type.${typeId}.desc`, { defaultValue: fallback });

/** Localized description of an input port: per-type key, then the shared `input._common.<port>`, then the catalog text. */
export const blockInputDesc = (typeId: string, port: string, fallback: string): string => {
  const specific = `blocks:input.${typeId}.${port}`;
  if (i18n.exists(specific)) return i18n.t(specific);
  const common = `blocks:input._common.${port}`;
  return i18n.exists(common) ? i18n.t(common) : fallback;
};

/** Localized description of a non-numeric option (helper text under the field); falls back to the catalog text. */
export const blockOptionDesc = (typeId: string, name: string, fallback: string): string => {
  const specific = `blocks:option.${typeId}.${name}.desc`;
  if (i18n.exists(specific)) return i18n.t(specific);
  const common = `blocks:option._common.${name}.desc`;
  return i18n.exists(common) ? i18n.t(common) : fallback;
};

// Human-readable parameter label/description: prefer a per-type i18n string (blocks:param.<type>.<name>),
// fall back to a shared governor entry (param._common), then to the catalog's raw name/English description.
export const paramLabel = (typeId: string, p: { name: string; unit?: string | null }): string => {
  const specific = `blocks:param.${typeId}.${p.name}.label`;
  const common = `blocks:param._common.${p.name}.label`;
  const base = i18n.exists(specific) ? i18n.t(specific) : i18n.exists(common) ? i18n.t(common) : p.name;
  return p.unit ? `${base} (${p.unit})` : base;
};

export const paramDesc = (typeId: string, p: { name: string; description: string }): string => {
  const specific = `blocks:param.${typeId}.${p.name}.desc`;
  const common = `blocks:param._common.${p.name}.desc`;
  return i18n.exists(specific) ? i18n.t(specific) : i18n.exists(common) ? i18n.t(common) : p.description;
};
