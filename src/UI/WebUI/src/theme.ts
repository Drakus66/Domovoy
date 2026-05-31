import { experimental_extendTheme as extendTheme } from '@mui/material/styles';

/**
 * Domovoy design system.
 *
 * Built with MUI's CSS-variables engine (`extendTheme`) so the app ships a real
 * light + dark theme that switches without a re-render and respects the OS setting.
 * Consumers read normal palette tokens (`primary.main`, `background.paper`, …) which
 * MUI maps to `--mui-*` CSS variables under <CssVarsProvider>.
 *
 * NOTE: the `breakpoints` block and the MuiButton / MuiIconButton / MuiSlider
 * style overrides are asserted verbatim by theme.test.ts — keep those literal.
 */
const theme = extendTheme({
  colorSchemes: {
    light: {
      palette: {
        primary: { main: '#4361EE', light: '#6B82F5', dark: '#2F49C9', contrastText: '#ffffff' },
        secondary: { main: '#00B4D8', light: '#48CAE4', dark: '#0096C7', contrastText: '#ffffff' },
        success: { main: '#2BAE66', light: '#5CC68C', dark: '#1E8A4F' },
        warning: { main: '#F6A609', light: '#FFC04D', dark: '#C98400' },
        error: { main: '#E5484D', light: '#EF7A7E', dark: '#C0353A' },
        info: { main: '#3B82F6', light: '#6BA1F8', dark: '#2563EB' },
        background: { default: '#EEF1F6', paper: '#FFFFFF' },
        text: {
          primary: '#1A1D23',
          secondary: '#5B6472',
          disabled: 'rgba(26, 29, 35, 0.38)',
        },
        divider: 'rgba(20, 23, 33, 0.08)',
        action: {
          hover: 'rgba(20, 23, 33, 0.04)',
          selected: 'rgba(67, 97, 238, 0.10)',
        },
      },
    },
    dark: {
      palette: {
        primary: { main: '#5B7CFF', light: '#859BFF', dark: '#3E5BE0', contrastText: '#ffffff' },
        secondary: { main: '#2DD4BF', light: '#5EEAD4', dark: '#14B8A6', contrastText: '#04201C' },
        success: { main: '#34D399', light: '#6EE7B7', dark: '#10B981' },
        warning: { main: '#FBBF24', light: '#FCD34D', dark: '#F59E0B' },
        error: { main: '#F87171', light: '#FCA5A5', dark: '#EF4444' },
        info: { main: '#60A5FA', light: '#93C5FD', dark: '#3B82F6' },
        background: { default: '#0D0F14', paper: '#171A21' },
        text: {
          primary: '#E6E8EE',
          secondary: '#9BA3B4',
          disabled: 'rgba(230, 232, 238, 0.38)',
        },
        divider: 'rgba(255, 255, 255, 0.08)',
        action: {
          hover: 'rgba(255, 255, 255, 0.05)',
          selected: 'rgba(91, 124, 255, 0.16)',
        },
      },
    },
  },
  typography: {
    fontFamily: [
      'Inter',
      '-apple-system',
      'BlinkMacSystemFont',
      '"Segoe UI"',
      'Roboto',
      '"Helvetica Neue"',
      'Arial',
      'sans-serif',
    ].join(','),
    h1: { fontSize: '2.25rem', fontWeight: 700, lineHeight: 1.2, letterSpacing: '-0.02em' },
    h2: { fontSize: '1.875rem', fontWeight: 700, lineHeight: 1.25, letterSpacing: '-0.02em' },
    h3: { fontSize: '1.5rem', fontWeight: 600, lineHeight: 1.3, letterSpacing: '-0.01em' },
    h4: { fontSize: '1.375rem', fontWeight: 600, lineHeight: 1.35, letterSpacing: '-0.01em' },
    h5: { fontSize: '1.125rem', fontWeight: 600, lineHeight: 1.4 },
    h6: { fontSize: '1rem', fontWeight: 600, lineHeight: 1.5 },
    body1: { fontSize: '0.95rem', lineHeight: 1.5 },
    body2: { fontSize: '0.85rem', lineHeight: 1.45 },
    caption: { fontSize: '0.75rem', lineHeight: 1.4 },
    button: { textTransform: 'none', fontWeight: 600 },
  },
  breakpoints: {
    values: {
      xs: 0,      // Mobile: 0-599px (single column)
      sm: 600,    // Tablet: 600-959px (two columns)
      md: 960,    // Desktop: 960-1279px (multi-column)
      lg: 1280,   // Large desktop: 1280-1919px
      xl: 1920,   // Extra large: 1920px+
    },
  },
  spacing: 8,
  shape: {
    borderRadius: 12,
  },
  components: {
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: {
          borderRadius: 10,
          padding: '8px 16px',
          // Touch-friendly minimum size
          minHeight: 44,
          minWidth: 44,
          fontWeight: 600,
        },
      },
    },
    MuiIconButton: {
      styleOverrides: {
        root: {
          // Touch-friendly minimum size
          minHeight: 44,
          minWidth: 44,
          borderRadius: 10,
        },
      },
    },
    MuiSlider: {
      styleOverrides: {
        root: {
          // Larger thumb for easier touch control
          '& .MuiSlider-thumb': {
            width: 20,
            height: 20,
          },
        },
      },
    },
    MuiCard: {
      defaultProps: { variant: 'outlined' },
      styleOverrides: {
        root: ({ theme: t }) => ({
          borderRadius: 16,
          backgroundImage: 'none',
          border: `1px solid ${t.vars?.palette.divider ?? t.palette.divider}`,
          boxShadow: 'none',
        }),
      },
    },
    MuiPaper: {
      styleOverrides: {
        root: {
          backgroundImage: 'none',
          borderRadius: 12,
        },
      },
    },
    MuiAppBar: {
      defaultProps: { elevation: 0, color: 'default' },
      styleOverrides: {
        root: ({ theme: t }) => ({
          backgroundImage: 'none',
          backgroundColor: t.vars?.palette.background.paper ?? t.palette.background.paper,
          color: t.vars?.palette.text.primary ?? t.palette.text.primary,
          boxShadow: 'none',
          borderBottom: `1px solid ${t.vars?.palette.divider ?? t.palette.divider}`,
        }),
      },
    },
    MuiDrawer: {
      styleOverrides: {
        paper: ({ theme: t }) => ({
          backgroundImage: 'none',
          borderColor: t.vars?.palette.divider ?? t.palette.divider,
        }),
      },
    },
    MuiSwitch: {
      styleOverrides: {
        root: {
          // Larger touch target for mobile
          padding: 8,
        },
      },
    },
    MuiChip: {
      styleOverrides: {
        root: { fontWeight: 600 },
      },
    },
    MuiTooltip: {
      styleOverrides: {
        tooltip: { fontSize: '0.75rem' },
      },
    },
  },
});

export default theme;
