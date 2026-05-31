import { IconButton, Tooltip } from '@mui/material';
import { useColorScheme } from '@mui/material/styles';
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded';
import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded';

/**
 * Light/dark switch backed by MUI's color-scheme engine.
 * Renders nothing meaningful (but does not crash) when used outside a
 * CssVarsProvider — e.g. in unit tests that wrap components in plain ThemeProvider.
 */
export default function ColorModeToggle() {
  const { mode, setMode } = useColorScheme();
  const isDark = mode === 'dark';

  return (
    <Tooltip title={isDark ? 'Switch to light' : 'Switch to dark'}>
      <IconButton
        aria-label="toggle color mode"
        onClick={() => setMode?.(isDark ? 'light' : 'dark')}
        color="inherit"
      >
        {isDark ? <LightModeRoundedIcon /> : <DarkModeRoundedIcon />}
      </IconButton>
    </Tooltip>
  );
}
