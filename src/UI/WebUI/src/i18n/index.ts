// Runtime i18n bootstrap.
//
// Translations are NOT bundled into the JS. They are fetched at runtime as static
// JSON from /locales/<lng>/<ns>.json (i18next-http-backend). This is the whole point
// of the setup: a language can be edited — or a new one added — by changing files
// under the deployed `locales/` folder (mounted as a volume in docker-compose), with
// NO `npm run build`. The nginx SPA config already serves these as static assets.
//
// Language is detected from localStorage (key `domovoy-lang`), falling back to the
// browser, then to Russian. Tests use a separate inline-resource init (see
// src/test/setup.ts) because http-backend has no server under jsdom.

import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import HttpBackend from 'i18next-http-backend';
import LanguageDetector from 'i18next-browser-languagedetector';
import { baseOptions } from './config';
import { LANGUAGE_STORAGE_KEY } from './languages';

i18n
  .use(HttpBackend)
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    ...baseOptions,
    backend: {
      loadPath: '/locales/{{lng}}/{{ns}}.json',
    },
    detection: {
      order: ['localStorage', 'navigator'],
      lookupLocalStorage: LANGUAGE_STORAGE_KEY,
      caches: ['localStorage'],
    },
    react: {
      useSuspense: true,
    },
  });

export default i18n;
