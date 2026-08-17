// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

import { useState } from 'react';
import {
  Box, IconButton, ListItemText, Menu, MenuItem, Tooltip, type MenuProps,
} from '@mui/material';
import TranslateRoundedIcon from '@mui/icons-material/TranslateRounded';
import CheckRoundedIcon from '@mui/icons-material/CheckRounded';
import { useTranslation } from 'react-i18next';
import { languages } from '../../i18n/languages';

interface LanguageMenuProps {
  anchorEl: HTMLElement | null;
  open: boolean;
  onClose: () => void;
  /** Fired after a language is picked (dismissing without a pick only fires onClose). */
  onSelected?: () => void;
  anchorOrigin?: MenuProps['anchorOrigin'];
  transformOrigin?: MenuProps['transformOrigin'];
}

/**
 * The language list as a controlled menu, reusable from any trigger — the translate icon
 * below and the profile dropdown's "Language" row. Selection goes through
 * i18next.changeLanguage, which persists to localStorage (key `domovoy-lang`).
 */
export function LanguageMenu({
  anchorEl, open, onClose, onSelected,
  anchorOrigin = { vertical: 'top', horizontal: 'right' },
  transformOrigin = { vertical: 'bottom', horizontal: 'right' },
}: LanguageMenuProps) {
  const { i18n } = useTranslation('common');
  const current = i18n.resolvedLanguage ?? i18n.language;

  return (
    <Menu
      anchorEl={anchorEl}
      open={open}
      onClose={onClose}
      anchorOrigin={anchorOrigin}
      transformOrigin={transformOrigin}
    >
      {languages.map((lng) => (
        <MenuItem
          key={lng.code}
          selected={lng.code === current}
          onClick={() => {
            i18n.changeLanguage(lng.code);
            onClose();
            onSelected?.();
          }}
          sx={{ gap: 1.5, minWidth: 200 }}
        >
          <Box component="span" sx={{ fontSize: '1.1rem', lineHeight: 1 }}>{lng.flag}</Box>
          <ListItemText primary={lng.nativeName} primaryTypographyProps={{ fontWeight: 600 }} />
          {lng.code === current && <CheckRoundedIcon fontSize="small" color="primary" />}
        </MenuItem>
      ))}
    </Menu>
  );
}

/**
 * Language switcher: a translate button opening the list of supported languages by
 * their endonyms.
 */
export default function LanguagePicker() {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);
  const { t } = useTranslation('common');

  return (
    <>
      <Tooltip title={t('language.label', 'Language')}>
        <IconButton aria-label={t('language.label', 'Language')} onClick={(e) => setAnchorEl(e.currentTarget)} color="inherit">
          <TranslateRoundedIcon />
        </IconButton>
      </Tooltip>
      <LanguageMenu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={() => setAnchorEl(null)} />
    </>
  );
}
