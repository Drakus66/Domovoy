import { useState } from 'react';
import { Box, IconButton, ListItemText, Menu, MenuItem, Tooltip } from '@mui/material';
import TranslateRoundedIcon from '@mui/icons-material/TranslateRounded';
import CheckRoundedIcon from '@mui/icons-material/CheckRounded';
import { useTranslation } from 'react-i18next';
import { languages } from '../../i18n/languages';

/**
 * Language switcher: a translate button opening the list of supported languages by
 * their endonyms. Selection goes through i18next.changeLanguage, which persists to
 * localStorage (key `domovoy-lang`) and lazy-loads any not-yet-fetched namespaces.
 */
export default function LanguagePicker() {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);
  const { t, i18n } = useTranslation('common');
  const current = i18n.resolvedLanguage ?? i18n.language;

  return (
    <>
      <Tooltip title={t('language.label', 'Language')}>
        <IconButton aria-label={t('language.label', 'Language')} onClick={(e) => setAnchorEl(e.currentTarget)} color="inherit">
          <TranslateRoundedIcon />
        </IconButton>
      </Tooltip>
      <Menu
        anchorEl={anchorEl}
        open={Boolean(anchorEl)}
        onClose={() => setAnchorEl(null)}
        anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
        transformOrigin={{ vertical: 'bottom', horizontal: 'right' }}
      >
        {languages.map((lng) => (
          <MenuItem
            key={lng.code}
            selected={lng.code === current}
            onClick={() => {
              i18n.changeLanguage(lng.code);
              setAnchorEl(null);
            }}
            sx={{ gap: 1.5, minWidth: 200 }}
          >
            <Box component="span" sx={{ fontSize: '1.1rem', lineHeight: 1 }}>{lng.flag}</Box>
            <ListItemText primary={lng.nativeName} primaryTypographyProps={{ fontWeight: 600 }} />
            {lng.code === current && <CheckRoundedIcon fontSize="small" color="primary" />}
          </MenuItem>
        ))}
      </Menu>
    </>
  );
}
