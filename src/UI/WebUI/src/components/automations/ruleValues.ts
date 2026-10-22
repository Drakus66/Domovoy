// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Value helpers shared by the rule editors and the Automations page. Kept out of RuleEditors.tsx so that
// module exports only components (Fast Refresh / react-refresh lint rule).

/** Comparison operators offered in the UI (shared by triggers and device conditions). */
export const OPERATORS = ['eq', 'ne', 'gt', 'lt', 'gte', 'lte', 'changed'];

/** Text field ⇄ typed value: "" / "true" → true, "false" → false, numeric → number, else the raw string. */
export const parseValue = (raw: string): unknown => {
  const s = raw.trim();
  if (s === '' || s.toLowerCase() === 'true') return true;
  if (s.toLowerCase() === 'false') return false;
  const n = Number(s);
  return Number.isNaN(n) ? s : n;
};

/** Typed value → display string (booleans as on/off), for the value text fields. */
export const fmt = (v: unknown): string => (typeof v === 'boolean' ? (v ? 'on' : 'off') : String(v ?? ''));
