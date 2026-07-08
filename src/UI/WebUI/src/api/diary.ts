// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import apiClient from './client';

/** One materialized diary day (matches DbGateway HomeStoryEntry, roadmap Epic 2N). */
export interface DiaryEntry {
  id: string;
  date: string;
  locale: string;
  paragraph: string;
  dayScore: number;
  tier: number;
}

export interface DiaryQuery {
  from?: string;
  to?: string;
  limit?: number;
}

/** A grammatically-marked synonym (matches the SynonymForm contract). */
export interface SynonymForm {
  text: string;
  gender?: string | null;
  number?: string | null;
  animacy?: string | null;
  prep?: string | null;
  locForm?: string | null;
  accForm?: string | null;
  subjectCase?: string | null;
  pronoun?: string | null;
}

/** A user-editable synonym pool override (matches the NarrativeEntity contract). */
export interface NarrativeEntity {
  id?: string;
  locale: string;
  kind: string; // persona | place | device
  key: string;
  synonyms: SynonymForm[];
}

const prune = (q: DiaryQuery) =>
  Object.fromEntries(Object.entries(q).filter(([, v]) => v !== undefined && v !== ''));

export const diaryApi = {
  /** The materialized diary feed (newest day first). */
  get: (q: DiaryQuery = {}): Promise<DiaryEntry[]> =>
    apiClient.get<DiaryEntry[]>('/api/home-story', { params: prune(q) }).then((r) => r.data),

  /** On-the-fly render of a single event/beat (Phase 0 preview). */
  preview: (body: unknown): Promise<{ paragraph: string; locale: string; renderer: string }> =>
    apiClient.post('/api/home-story/preview', body).then((r) => r.data),

  /** Force-rebuild one day (yyyy-MM-dd). */
  rebuild: (date: string): Promise<unknown> =>
    apiClient.post(`/api/home-story/rebuild?date=${encodeURIComponent(date)}`).then((r) => r.data),

  /** Personalization overrides for the given locale. */
  entities: (locale = 'ru', kind?: string): Promise<NarrativeEntity[]> =>
    apiClient
      .get<NarrativeEntity[]>('/api/narrative-entities', { params: prune({ locale, kind } as never) })
      .then((r) => r.data),

  /** Create or update a personalization override. */
  saveEntity: (e: NarrativeEntity): Promise<NarrativeEntity | void> =>
    e.id
      ? apiClient.put(`/api/narrative-entities/${e.id}`, e).then(() => undefined)
      : apiClient.post<NarrativeEntity>('/api/narrative-entities', e).then((r) => r.data),
};
