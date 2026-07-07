// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Supported UI languages. Data-driven so adding a language later is a matter of
// (1) adding an entry here and (2) dropping public/locales/<code>/*.json — no code
// change in the picker or the i18n init. Russian is the product default (the house
// spirit speaks Russian first); English is the parallel translation.

export interface Language {
  /** BCP-47 code, also the folder name under public/locales/. */
  code: string;
  /** Endonym — shown in the picker in the language's own script. */
  nativeName: string;
  /** Short flag/emoji hint for the menu. */
  flag: string;
}

export const languages: Language[] = [
  { code: 'ru', nativeName: 'Русский', flag: '🇷🇺' },
  { code: 'en', nativeName: 'English', flag: '🇬🇧' },
];

export const DEFAULT_LANGUAGE = 'ru';
export const LANGUAGE_STORAGE_KEY = 'domovoy-lang';

export const supportedLngs = languages.map((l) => l.code);
