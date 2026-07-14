// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { IconButton, Tooltip } from '@mui/material';
import { useColorScheme } from '@mui/material/styles';
import { useTranslation } from 'react-i18next';
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded';
import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded';

/**
 * Light/dark switch backed by MUI's color-scheme engine.
 * Renders nothing meaningful (but does not crash) when used outside a
 * CssVarsProvider — e.g. in unit tests that wrap components in plain ThemeProvider.
 */
export default function ColorModeToggle() {
  const { mode, setMode } = useColorScheme();
  const { t } = useTranslation('common');
  const isDark = mode === 'dark';

  return (
    <Tooltip title={isDark ? t('colorMode.toLight') : t('colorMode.toDark')}>
      <IconButton
        aria-label={t('colorMode.ariaLabel')}
        onClick={() => setMode?.(isDark ? 'light' : 'dark')}
        color="inherit"
      >
        {isDark ? <LightModeRoundedIcon /> : <DarkModeRoundedIcon />}
      </IconButton>
    </Tooltip>
  );
}
