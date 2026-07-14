// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** The installation's site location (roadmap Epic 2K) — coordinates drive sunrise/sunset locally. */
export interface SiteLocation {
  id: string;
  latitude: number;
  longitude: number;
  label: string | null;
  timeZoneId: string | null;
  timeZoneAuto: boolean;
  updatedAt: string;
}

/** What the editor sends back on save (mirrors the DbGateway LocationUpdate record). */
export interface LocationUpdate {
  latitude: number;
  longitude: number;
  label: string | null;
  timeZoneId: string | null;
  timeZoneAuto: boolean;
}

/** One geocoder candidate: a place name resolved to coordinates (online-only, optional). */
export interface GeocodeResult {
  label: string;
  latitude: number;
  longitude: number;
}

/** Calendar sensor settings (roadmap Epic 2L). WeekendDays are System.DayOfWeek ints (0=Sun…6=Sat). */
export interface CalendarSettings {
  id: string;
  weekendDays: number[];
  holidays: string[];
  updatedAt: string;
}

export const settingsApi = {
  getLocation: (): Promise<SiteLocation> =>
    apiClient.get<SiteLocation>('/api/settings/location').then((r) => r.data),

  saveLocation: (body: LocationUpdate): Promise<SiteLocation> =>
    apiClient.put<SiteLocation>('/api/settings/location', body).then((r) => r.data),

  // Offline timezone lookup from coordinates — previews the auto tz before the user saves.
  getTimezone: (lat: number, lon: number): Promise<string | null> =>
    apiClient
      .get<{ timeZoneId: string | null }>('/api/settings/timezone', { params: { lat, lon } })
      .then((r) => r.data.timeZoneId),

  // Forward-geocode a place name (online-only; resolves to [] when offline).
  geocode: (q: string): Promise<GeocodeResult[]> =>
    apiClient.get<GeocodeResult[]>('/api/settings/geocode', { params: { q } }).then((r) => r.data),

  // Reverse-geocode a map click into a display label (online-only; null when offline).
  reverseGeocode: (lat: number, lon: number): Promise<string | null> =>
    apiClient
      .get<{ label: string | null }>('/api/settings/reverse-geocode', { params: { lat, lon } })
      .then((r) => r.data.label),

  getCalendar: (): Promise<CalendarSettings> =>
    apiClient.get<CalendarSettings>('/api/settings/calendar').then((r) => r.data),

  saveCalendar: (body: { weekendDays: number[]; holidays: string[] }): Promise<CalendarSettings> =>
    apiClient.put<CalendarSettings>('/api/settings/calendar', body).then((r) => r.data),

  // Merge a country's public holidays for a year (online-only). Returns how many were imported.
  importHolidays: (country: string, year: number): Promise<{ imported: number; settings: CalendarSettings }> =>
    apiClient
      .post<{ imported: number; settings: CalendarSettings }>('/api/settings/calendar/import', null, {
        params: { country, year },
      })
      .then((r) => r.data),
};
