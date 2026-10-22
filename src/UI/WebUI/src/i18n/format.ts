// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Locale-aware formatting helpers that follow the ACTIVE UI language, not the
// browser's. They read i18n.language at call time (i18next is a singleton), so a
// language switch re-formats dates/numbers on the next render. Native Intl handles
// 'ru'/'en' directly — no extra date library needed for this.

import i18n from 'i18next';

/** Format an ISO string as a localized date+time; passes the raw value through if unparseable. */
export function fmtDateTime(iso?: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString(i18n.language);
}

/** Format an epoch-ms or ISO value with explicit Intl options in the active locale. */
export function fmtDate(value: string | number | Date, options?: Intl.DateTimeFormatOptions): string {
  const d = value instanceof Date ? value : new Date(value);
  return Number.isNaN(d.getTime()) ? String(value) : d.toLocaleDateString(i18n.language, options);
}

/** Format a time-of-day in the active locale. */
export function fmtTime(value: string | number | Date, options?: Intl.DateTimeFormatOptions): string {
  const d = value instanceof Date ? value : new Date(value);
  return Number.isNaN(d.getTime()) ? String(value) : d.toLocaleTimeString(i18n.language, options);
}

/** Compact "time since" for chips/captions ("30 с", "5 мин", "2 ч", "3 д"); localized via common:relative.*. */
export function fmtRelativeShort(value: string | number | Date): string {
  const d = value instanceof Date ? value : new Date(value);
  const ms = d.getTime();
  if (Number.isNaN(ms)) return String(value);
  const sec = Math.max(0, Math.round((Date.now() - ms) / 1000));
  if (sec < 10) return i18n.t('common:relative.now');
  if (sec < 60) return i18n.t('common:relative.sec', { n: sec });
  const min = Math.round(sec / 60);
  if (min < 60) return i18n.t('common:relative.min', { n: min });
  const hour = Math.round(min / 60);
  if (hour < 24) return i18n.t('common:relative.hour', { n: hour });
  return i18n.t('common:relative.day', { n: Math.round(hour / 24) });
}
