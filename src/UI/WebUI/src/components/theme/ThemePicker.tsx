import { useState } from 'react';
import {
  Box, IconButton, ListItemText, Menu, MenuItem, Tooltip,
} from '@mui/material';
import { useColorScheme } from '@mui/material/styles';
import { useTranslation } from 'react-i18next';
import PaletteRoundedIcon from '@mui/icons-material/PaletteRounded';
import CheckRoundedIcon from '@mui/icons-material/CheckRounded';
import { themeOptions, type ThemePreview } from '../../theme';
import { useThemeStore } from '../../store/themeStore';

function Swatch({ preview }: { preview: ThemePreview }) {
  return (
    <Box
      sx={{
        width: 40, height: 24, borderRadius: 1.5, flexShrink: 0,
        display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 0.5,
        bgcolor: preview.background,
        border: '1px solid', borderColor: 'divider',
      }}
    >
      <Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: preview.primary }} />
      <Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: preview.secondary }} />
    </Box>
  );
}

/**
 * Fixed-theme picker: a palette button opening a menu of the curated themes,
 * each previewed with its background + primary/secondary swatches for the
 * current light/dark mode. Selection is persisted by useThemeStore.
 */
export default function ThemePicker() {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);
  const themeId = useThemeStore((s) => s.themeId);
  const setThemeId = useThemeStore((s) => s.setThemeId);
  const { mode, systemMode } = useColorScheme();
  const { t } = useTranslation('common');
  const resolvedMode = (mode === 'system' ? systemMode : mode) ?? 'dark';

  return (
    <>
      <Tooltip title={t('theme.label')}>
        <IconButton aria-label={t('theme.label')} onClick={(e) => setAnchorEl(e.currentTarget)} color="inherit">
          <PaletteRoundedIcon />
        </IconButton>
      </Tooltip>
      <Menu
        anchorEl={anchorEl}
        open={Boolean(anchorEl)}
        onClose={() => setAnchorEl(null)}
        anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
        transformOrigin={{ vertical: 'bottom', horizontal: 'right' }}
      >
        {themeOptions.map((option) => (
          <MenuItem
            key={option.id}
            selected={option.id === themeId}
            onClick={() => {
              setThemeId(option.id);
              setAnchorEl(null);
            }}
            sx={{ gap: 1.5, minWidth: 280 }}
          >
            <Swatch preview={option.preview[resolvedMode]} />
            <ListItemText
              primary={option.label}
              secondary={t(`themes.${option.id}`, option.description)}
              primaryTypographyProps={{ fontWeight: 600 }}
              secondaryTypographyProps={{ variant: 'caption' }}
            />
            {option.id === themeId && <CheckRoundedIcon fontSize="small" color="primary" />}
          </MenuItem>
        ))}
      </Menu>
    </>
  );
}
