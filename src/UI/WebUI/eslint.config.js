// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

// Flat config — обязательный формат начиная с ESLint 9 (прежний .eslintrc.cjs больше не читается).
// Набор правил перенесён один в один, чтобы апгрейд не превратился в незаметную смену требований
// к коду: ужесточать линтер — отдельная работа, а не побочный эффект закрытия уязвимостей.

import js from '@eslint/js';
import globals from 'globals';
import tseslint from 'typescript-eslint';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';

export default tseslint.config(
  // dist — артефакт сборки, node_modules — чужой код. В flat config игноры задаются здесь,
  // а не в .eslintignore (он тоже больше не читается).
  { ignores: ['dist/**', 'node_modules/**', 'dev-dist/**'] },

  js.configs.recommended,
  ...tseslint.configs.recommended,

  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      // Ровно те два правила, что давал react-hooks@4 через plugin:react-hooks/recommended.
      // В 7-й версии `recommended` разросся (set-state-in-effect, purity, refs и другие) и включает
      // требования, которых код никогда не проходил. Принимать их разом — значит смешать закрытие
      // уязвимостей с ужесточением линтера и получить 60 «ошибок» на ровном месте.
      // Включение нового набора — отдельная осознанная работа (техдолг Фазы 4).
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'warn',

      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      '@typescript-eslint/no-explicit-any': 'warn',
    },
  },

  // Тесты и настройка окружения: здесь живут глобальные vitest-функции и node-API.
  {
    files: ['**/*.test.{ts,tsx}', 'src/test/**/*.{ts,tsx}', '*.config.{ts,js}'],
    languageOptions: {
      globals: { ...globals.node, ...globals.browser },
    },
  },
);
