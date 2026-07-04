import { experimental_extendTheme as extendTheme } from '@mui/material/styles';
import type { CssVarsThemeOptions, PaletteOptions } from '@mui/material/styles';

/**
 * Domovoy design system.
 *
 * Built with MUI's CSS-variables engine (`extendTheme`) so every theme ships a real
 * light + dark scheme that switches without a re-render and respects the OS setting.
 * Consumers read normal palette tokens (`primary.main`, `background.paper`, …) which
 * MUI maps to `--mui-*` CSS variables under <CssVarsProvider>.
 *
 * The app ships a fixed set of curated themes (see `themeOptions`). Every theme shares
 * the same structural base (typography, breakpoints, component overrides) and differs
 * only in palette. The selected theme id is persisted by `useThemeStore`
 * (localStorage `domovoy-theme`); light/dark mode stays with MUI's own color-scheme
 * storage (`domovoy-color-mode`).
 *
 * NOTE: the `breakpoints` block and the MuiButton / MuiIconButton / MuiSlider
 * style overrides are asserted verbatim by theme.test.ts — keep those literal.
 */
const base: Omit<CssVarsThemeOptions, 'colorSchemes'> = {
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
};

// Shared status colors. Themes whose accent is gold/amber swap `warning` for the
// orange variants below so warnings stay visually distinct from the accent.
const statusLight = {
  success: { main: '#2BAE66', light: '#5CC68C', dark: '#1E8A4F' },
  warning: { main: '#F6A609', light: '#FFC04D', dark: '#C98400' },
  error: { main: '#E5484D', light: '#EF7A7E', dark: '#C0353A' },
  info: { main: '#3B82F6', light: '#6BA1F8', dark: '#2563EB' },
};
const statusDark = {
  success: { main: '#34D399', light: '#6EE7B7', dark: '#10B981' },
  warning: { main: '#FBBF24', light: '#FCD34D', dark: '#F59E0B' },
  error: { main: '#F87171', light: '#FCA5A5', dark: '#EF4444' },
  info: { main: '#60A5FA', light: '#93C5FD', dark: '#3B82F6' },
};
const orangeWarningLight = { warning: { main: '#F97316', light: '#FB923C', dark: '#C2410C' } };
const orangeWarningDark = { warning: { main: '#FB923C', light: '#FDBA74', dark: '#F97316' } };

export type ThemeId =
  | 'domovoy'
  | 'skazka'
  | 'jarvis'
  | 'cosmos'
  | 'aurora'
  | 'malachite'
  | 'gzhel'
  | 'nisse'
  | 'zashiki'
  | 'alux'
  | 'brownie'
  | 'cluricaun';

export interface ThemePreview {
  primary: string;
  secondary: string;
  background: string;
}

export interface ThemeOption {
  id: ThemeId;
  label: string;
  description: string;
  preview: { light: ThemePreview; dark: ThemePreview };
  theme: ReturnType<typeof extendTheme>;
}

function buildTheme(light: PaletteOptions, dark: PaletteOptions) {
  return extendTheme({
    colorSchemes: {
      light: { palette: light },
      dark: { palette: dark },
    },
    ...base,
  });
}

export const themeOptions: ThemeOption[] = [
  {
    // Default hearth palette: the original indigo primary, honey-amber accent.
    id: 'domovoy',
    label: 'Domovoy',
    description: 'Indigo & warm honey — the hearth default',
    preview: {
      light: { primary: '#4361EE', secondary: '#CE8620', background: '#EEF1F6' },
      dark: { primary: '#5B7CFF', secondary: '#EDAE49', background: '#0D0F14' },
    },
    theme: buildTheme(
      {
        primary: { main: '#4361EE', light: '#6B82F5', dark: '#2F49C9', contrastText: '#ffffff' },
        secondary: { main: '#CE8620', light: '#E3A94E', dark: '#A76A0F', contrastText: '#2A1C04' },
        ...statusLight,
        ...orangeWarningLight,
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
      {
        primary: { main: '#5B7CFF', light: '#859BFF', dark: '#3E5BE0', contrastText: '#ffffff' },
        secondary: { main: '#EDAE49', light: '#F5C877', dark: '#C98A20', contrastText: '#2A1C04' },
        ...statusDark,
        ...orangeWarningDark,
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
    ),
  },
  {
    // Russian folk tale: khokhloma cinnabar + gold on birch parchment / lacquered night.
    id: 'skazka',
    label: 'Lukomorye',
    description: 'Folk tale: khokhloma red, gold & birch',
    preview: {
      light: { primary: '#B7402A', secondary: '#B58712', background: '#F5EEDF' },
      dark: { primary: '#E2593C', secondary: '#EFB53B', background: '#151009' },
    },
    theme: buildTheme(
      {
        primary: { main: '#B7402A', light: '#D06A52', dark: '#8F2E1C', contrastText: '#FFFFFF' },
        secondary: { main: '#B58712', light: '#D2A83E', dark: '#8C6708', contrastText: '#2A1C04' },
        ...statusLight,
        ...orangeWarningLight,
        background: { default: '#F5EEDF', paper: '#FFFBF2' },
        text: {
          primary: '#2C2015',
          secondary: '#6E5D48',
          disabled: 'rgba(44, 32, 21, 0.38)',
        },
        divider: 'rgba(70, 50, 25, 0.12)',
        action: {
          hover: 'rgba(70, 50, 25, 0.05)',
          selected: 'rgba(183, 64, 42, 0.10)',
        },
      },
      {
        primary: { main: '#E2593C', light: '#F08265', dark: '#B93E24', contrastText: '#FFFFFF' },
        secondary: { main: '#EFB53B', light: '#F6CE71', dark: '#C88F14', contrastText: '#2B1D02' },
        ...statusDark,
        ...orangeWarningDark,
        background: { default: '#151009', paper: '#211810' },
        text: {
          primary: '#F0E6D6',
          secondary: '#B3A288',
          disabled: 'rgba(240, 230, 214, 0.38)',
        },
        divider: 'rgba(240, 224, 190, 0.10)',
        action: {
          hover: 'rgba(240, 224, 190, 0.05)',
          selected: 'rgba(226, 89, 60, 0.16)',
        },
      },
    ),
  },
  {
    // Butler-grade AI HUD: graphite-blue surfaces, cyan readouts, brass accents.
    id: 'jarvis',
    label: 'Jarvis',
    description: 'Butler-grade HUD: graphite, cyan & brass',
    preview: {
      light: { primary: '#0284C7', secondary: '#BE8E13', background: '#EDF2F7' },
      dark: { primary: '#38BDF8', secondary: '#F5C044', background: '#0A0E14' },
    },
    theme: buildTheme(
      {
        primary: { main: '#0284C7', light: '#38BDF8', dark: '#075985', contrastText: '#FFFFFF' },
        secondary: { main: '#BE8E13', light: '#DCAE3C', dark: '#93690C', contrastText: '#241A02' },
        ...statusLight,
        ...orangeWarningLight,
        background: { default: '#EDF2F7', paper: '#FFFFFF' },
        text: {
          primary: '#0F1B26',
          secondary: '#4E6272',
          disabled: 'rgba(15, 27, 38, 0.38)',
        },
        divider: 'rgba(10, 40, 70, 0.10)',
        action: {
          hover: 'rgba(10, 40, 70, 0.04)',
          selected: 'rgba(2, 132, 199, 0.10)',
        },
      },
      {
        primary: { main: '#38BDF8', light: '#7DD3FC', dark: '#0EA5E9', contrastText: '#05121C' },
        secondary: { main: '#F5C044', light: '#F8D57E', dark: '#D9A017', contrastText: '#241A02' },
        ...statusDark,
        ...orangeWarningDark,
        background: { default: '#0A0E14', paper: '#101722' },
        text: {
          primary: '#D7E3F0',
          secondary: '#8CA1B6',
          disabled: 'rgba(215, 227, 240, 0.38)',
        },
        divider: 'rgba(125, 211, 252, 0.12)',
        action: {
          hover: 'rgba(125, 211, 252, 0.06)',
          selected: 'rgba(56, 189, 248, 0.16)',
        },
      },
    ),
  },
  {
    // Deep space: nebula violet + starlight cyan on near-black indigo.
    id: 'cosmos',
    label: 'Cosmos',
    description: 'Deep space: nebula violet & starlight cyan',
    preview: {
      light: { primary: '#6D5BD0', secondary: '#0891B2', background: '#EEF0FA' },
      dark: { primary: '#8F7BFF', secondary: '#4CC9F0', background: '#0B0D1A' },
    },
    theme: buildTheme(
      {
        primary: { main: '#6D5BD0', light: '#8F7FE0', dark: '#5243A8', contrastText: '#FFFFFF' },
        secondary: { main: '#0891B2', light: '#22B8DB', dark: '#0E7490', contrastText: '#FFFFFF' },
        ...statusLight,
        background: { default: '#EEF0FA', paper: '#FFFFFF' },
        text: {
          primary: '#1B1D2E',
          secondary: '#5A5F7A',
          disabled: 'rgba(27, 29, 46, 0.38)',
        },
        divider: 'rgba(40, 40, 90, 0.10)',
        action: {
          hover: 'rgba(40, 40, 90, 0.04)',
          selected: 'rgba(109, 91, 208, 0.10)',
        },
      },
      {
        primary: { main: '#8F7BFF', light: '#B3A4FF', dark: '#6C55E6', contrastText: '#FFFFFF' },
        secondary: { main: '#4CC9F0', light: '#7EDCF6', dark: '#22A6CF', contrastText: '#052530' },
        ...statusDark,
        background: { default: '#0B0D1A', paper: '#151829' },
        text: {
          primary: '#E4E6F5',
          secondary: '#9AA0C3',
          disabled: 'rgba(228, 230, 245, 0.38)',
        },
        divider: 'rgba(179, 164, 255, 0.10)',
        action: {
          hover: 'rgba(179, 164, 255, 0.05)',
          selected: 'rgba(143, 123, 255, 0.18)',
        },
      },
    ),
  },
  {
    // Polar night: neon aurora-green glow + violet over a blue-black night sky.
    // Green ladder (hue/tone spread across the green themes): alux turquoise H≈172 →
    // malachite deep emerald H≈151 → aurora neon spring green H≈140 → cluricaun warm clover H≈123.
    id: 'aurora',
    label: 'Aurora',
    description: 'Polar night: aurora glow & violet',
    preview: {
      light: { primary: '#1FA65A', secondary: '#7C5CD6', background: '#EDF2F6' },
      dark: { primary: '#67E893', secondary: '#A78BFA', background: '#0B1118' },
    },
    theme: buildTheme(
      {
        primary: { main: '#1FA65A', light: '#4FC581', dark: '#158043', contrastText: '#FFFFFF' },
        secondary: { main: '#7C5CD6', light: '#9B82E3', dark: '#5F41B5', contrastText: '#FFFFFF' },
        ...statusLight,
        background: { default: '#EDF2F6', paper: '#FFFFFF' },
        text: {
          primary: '#16202A',
          secondary: '#4F6272',
          disabled: 'rgba(22, 32, 42, 0.38)',
        },
        divider: 'rgba(20, 50, 80, 0.10)',
        action: {
          hover: 'rgba(20, 50, 80, 0.04)',
          selected: 'rgba(31, 166, 90, 0.10)',
        },
      },
      {
        primary: { main: '#67E893', light: '#95F0B5', dark: '#3DC46F', contrastText: '#04240F' },
        secondary: { main: '#A78BFA', light: '#C4B0FC', dark: '#8B5CF6', contrastText: '#1D1233' },
        ...statusDark,
        background: { default: '#0B1118', paper: '#141D28' },
        text: {
          primary: '#E3EBF2',
          secondary: '#93A4B5',
          disabled: 'rgba(227, 235, 242, 0.38)',
        },
        divider: 'rgba(149, 240, 181, 0.10)',
        action: {
          hover: 'rgba(149, 240, 181, 0.05)',
          selected: 'rgba(103, 232, 147, 0.16)',
        },
      },
    ),
  },
  {
    // Bazhov's Ural tales: malachite stone green + copper veins.
    id: 'malachite',
    label: 'Malachite',
    description: 'Ural tales: malachite green & copper',
    preview: {
      light: { primary: '#11744B', secondary: '#B4682F', background: '#EFF4F0' },
      dark: { primary: '#2BA169', secondary: '#D6905A', background: '#0C1412' },
    },
    theme: buildTheme(
      {
        primary: { main: '#11744B', light: '#3D9A72', dark: '#0B5636', contrastText: '#FFFFFF' },
        secondary: { main: '#B4682F', light: '#CC8A54', dark: '#8F4E1D', contrastText: '#FFFFFF' },
        ...statusLight,
        background: { default: '#EFF4F0', paper: '#FFFFFF' },
        text: {
          primary: '#182420',
          secondary: '#54665E',
          disabled: 'rgba(24, 36, 32, 0.38)',
        },
        divider: 'rgba(20, 50, 40, 0.10)',
        action: {
          hover: 'rgba(20, 50, 40, 0.04)',
          selected: 'rgba(17, 116, 75, 0.10)',
        },
      },
      {
        primary: { main: '#2BA169', light: '#57BE8D', dark: '#1D7F50', contrastText: '#03271A' },
        secondary: { main: '#D6905A', light: '#E4B084', dark: '#B26F3B', contrastText: '#2B1502' },
        ...statusDark,
        background: { default: '#0C1412', paper: '#14221E' },
        text: {
          primary: '#E3EFE8',
          secondary: '#9BB2A6',
          disabled: 'rgba(227, 239, 232, 0.38)',
        },
        divider: 'rgba(160, 210, 190, 0.10)',
        action: {
          hover: 'rgba(160, 210, 190, 0.05)',
          selected: 'rgba(43, 161, 105, 0.16)',
        },
      },
    ),
  },
  {
    // Gzhel porcelain: cobalt brushwork on white; night-glaze blues in dark mode.
    id: 'gzhel',
    label: 'Gzhel',
    description: 'Porcelain: cobalt brushwork on white',
    preview: {
      light: { primary: '#2155A4', secondary: '#3E87CE', background: '#F1F5FA' },
      dark: { primary: '#6FA0E8', secondary: '#8FC7EF', background: '#0D131E' },
    },
    theme: buildTheme(
      {
        primary: { main: '#2155A4', light: '#4E7BC4', dark: '#153E7E', contrastText: '#FFFFFF' },
        secondary: { main: '#3E87CE', light: '#6FAADF', dark: '#2A69A8', contrastText: '#FFFFFF' },
        ...statusLight,
        background: { default: '#F1F5FA', paper: '#FFFFFF' },
        text: {
          primary: '#16233A',
          secondary: '#526A85',
          disabled: 'rgba(22, 35, 58, 0.38)',
        },
        divider: 'rgba(25, 60, 110, 0.10)',
        action: {
          hover: 'rgba(25, 60, 110, 0.04)',
          selected: 'rgba(33, 85, 164, 0.10)',
        },
      },
      {
        primary: { main: '#6FA0E8', light: '#9ABFF2', dark: '#4A7ECB', contrastText: '#071528' },
        secondary: { main: '#8FC7EF', light: '#B6DCF6', dark: '#5FA8DC', contrastText: '#08283D' },
        ...statusDark,
        background: { default: '#0D131E', paper: '#16202E' },
        text: {
          primary: '#E3EAF5',
          secondary: '#93A2B8',
          disabled: 'rgba(227, 234, 245, 0.38)',
        },
        divider: 'rgba(154, 191, 242, 0.10)',
        action: {
          hover: 'rgba(154, 191, 242, 0.05)',
          selected: 'rgba(111, 160, 232, 0.16)',
        },
      },
    ),
  },
  {
    // Scandinavian house gnome: falu-red barns, winter night, straw & spruce.
    id: 'nisse',
    label: 'Nisse',
    description: 'Winter farmstead: falu red, snow & straw',
    preview: {
      light: { primary: '#9A3B32', secondary: '#3E6B4F', background: '#F3F5F7' },
      dark: { primary: '#D95B4E', secondary: '#DDB25F', background: '#0E1216' },
    },
    theme: buildTheme(
      {
        primary: { main: '#9A3B32', light: '#B96157', dark: '#762A23', contrastText: '#FFFFFF' },
        secondary: { main: '#3E6B4F', light: '#5F8C70', dark: '#2A4F38', contrastText: '#FFFFFF' },
        ...statusLight,
        ...orangeWarningLight,
        background: { default: '#F3F5F7', paper: '#FFFFFF' },
        text: {
          primary: '#1C2126',
          secondary: '#59646E',
          disabled: 'rgba(28, 33, 38, 0.38)',
        },
        divider: 'rgba(30, 40, 50, 0.10)',
        action: {
          hover: 'rgba(30, 40, 50, 0.04)',
          selected: 'rgba(154, 59, 50, 0.10)',
        },
      },
      {
        primary: { main: '#D95B4E', light: '#E68579', dark: '#B24237', contrastText: '#FFFFFF' },
        secondary: { main: '#DDB25F', light: '#EAC98B', dark: '#BE9139', contrastText: '#281B03' },
        ...statusDark,
        ...orangeWarningDark,
        background: { default: '#0E1216', paper: '#171D24' },
        text: {
          primary: '#E4E9EF',
          secondary: '#98A5B3',
          disabled: 'rgba(228, 233, 239, 0.38)',
        },
        divider: 'rgba(220, 230, 240, 0.09)',
        action: {
          hover: 'rgba(220, 230, 240, 0.05)',
          selected: 'rgba(217, 91, 78, 0.16)',
        },
      },
    ),
  },
  {
    // Japanese child-spirit of the parlor: washi paper, aizome indigo, vermilion, matcha.
    id: 'zashiki',
    label: 'Zashiki-warashi',
    description: 'Tatami & indigo: washi, vermilion, matcha',
    preview: {
      light: { primary: '#35507E', secondary: '#C24F35', background: '#F6F2E7' },
      dark: { primary: '#E06A50', secondary: '#9FB86A', background: '#10141C' },
    },
    theme: buildTheme(
      {
        primary: { main: '#35507E', light: '#5D75A0', dark: '#243A61', contrastText: '#FFFFFF' },
        secondary: { main: '#C24F35', light: '#D57760', dark: '#9C3A24', contrastText: '#FFFFFF' },
        ...statusLight,
        background: { default: '#F6F2E7', paper: '#FDFBF4' },
        text: {
          primary: '#26221A',
          secondary: '#665E4E',
          disabled: 'rgba(38, 34, 26, 0.38)',
        },
        divider: 'rgba(50, 45, 30, 0.12)',
        action: {
          hover: 'rgba(50, 45, 30, 0.05)',
          selected: 'rgba(53, 80, 126, 0.10)',
        },
      },
      {
        primary: { main: '#E06A50', light: '#EC937F', dark: '#BC4F37', contrastText: '#FFFFFF' },
        secondary: { main: '#9FB86A', light: '#BCCE92', dark: '#7E9A48', contrastText: '#1B2306' },
        ...statusDark,
        background: { default: '#10141C', paper: '#182030' },
        text: {
          primary: '#E7E9F0',
          secondary: '#9AA2B8',
          disabled: 'rgba(231, 233, 240, 0.38)',
        },
        divider: 'rgba(190, 200, 230, 0.10)',
        action: {
          hover: 'rgba(190, 200, 230, 0.05)',
          selected: 'rgba(224, 106, 80, 0.16)',
        },
      },
    ),
  },
  {
    // Maya household guardian: cenote turquoise in limestone, maize gold, obsidian night.
    id: 'alux',
    label: 'Alux',
    description: 'Maya guardian: cenote turquoise & maize',
    preview: {
      light: { primary: '#17897B', secondary: '#B98A1D', background: '#F3EDDC' },
      dark: { primary: '#35C0AE', secondary: '#E2B54A', background: '#0E1413' },
    },
    theme: buildTheme(
      {
        primary: { main: '#17897B', light: '#43A99B', dark: '#0E685D', contrastText: '#FFFFFF' },
        secondary: { main: '#B98A1D', light: '#D2A845', dark: '#8F6910', contrastText: '#271B02' },
        ...statusLight,
        ...orangeWarningLight,
        background: { default: '#F3EDDC', paper: '#FCF8EE' },
        text: {
          primary: '#242017',
          secondary: '#635C49',
          disabled: 'rgba(36, 32, 23, 0.38)',
        },
        divider: 'rgba(60, 50, 25, 0.12)',
        action: {
          hover: 'rgba(60, 50, 25, 0.05)',
          selected: 'rgba(23, 137, 123, 0.10)',
        },
      },
      {
        primary: { main: '#35C0AE', light: '#67D4C6', dark: '#219B8B', contrastText: '#03211D' },
        secondary: { main: '#E2B54A', light: '#EDCB7B', dark: '#C09426', contrastText: '#271B02' },
        ...statusDark,
        ...orangeWarningDark,
        background: { default: '#0E1413', paper: '#172220' },
        text: {
          primary: '#E2EEEB',
          secondary: '#96ACA7',
          disabled: 'rgba(226, 238, 235, 0.38)',
        },
        divider: 'rgba(160, 215, 205, 0.10)',
        action: {
          hover: 'rgba(160, 215, 205, 0.05)',
          selected: 'rgba(53, 192, 174, 0.16)',
        },
      },
    ),
  },
  {
    // Scottish night helper: peat, heather purple, oat parchment & caramel.
    id: 'brownie',
    label: 'Brownie',
    description: 'Scottish hearth: heather, oat & caramel',
    preview: {
      light: { primary: '#7B5AA6', secondary: '#B0742E', background: '#F4EDDF' },
      dark: { primary: '#9C7BC0', secondary: '#C98A4B', background: '#141009' },
    },
    theme: buildTheme(
      {
        primary: { main: '#7B5AA6', light: '#9B7FC0', dark: '#5D4283', contrastText: '#FFFFFF' },
        secondary: { main: '#B0742E', light: '#C7924F', dark: '#8A5A1E', contrastText: '#FFFFFF' },
        ...statusLight,
        background: { default: '#F4EDDF', paper: '#FCF8EF' },
        text: {
          primary: '#28211A',
          secondary: '#6A5F52',
          disabled: 'rgba(40, 33, 26, 0.38)',
        },
        divider: 'rgba(60, 45, 25, 0.12)',
        action: {
          hover: 'rgba(60, 45, 25, 0.05)',
          selected: 'rgba(123, 90, 166, 0.10)',
        },
      },
      {
        primary: { main: '#9C7BC0', light: '#B79DD4', dark: '#7C5AA3', contrastText: '#FFFFFF' },
        secondary: { main: '#C98A4B', light: '#DBA976', dark: '#A76B2F', contrastText: '#2A1A04' },
        ...statusDark,
        background: { default: '#141009', paper: '#1E1812' },
        text: {
          primary: '#EDE7DC',
          secondary: '#AFA391',
          disabled: 'rgba(237, 231, 220, 0.38)',
        },
        divider: 'rgba(235, 225, 210, 0.10)',
        action: {
          hover: 'rgba(235, 225, 210, 0.05)',
          selected: 'rgba(156, 123, 192, 0.16)',
        },
      },
    ),
  },
  {
    // Irish cellar spirit: stout black-brown, warm clover green, creamy foam & whiskey.
    id: 'cluricaun',
    label: 'Cluricaun',
    description: 'Irish cellar: stout, clover & whiskey',
    preview: {
      light: { primary: '#3B7A2A', secondary: '#AD6E22', background: '#F1F1E8' },
      dark: { primary: '#4CB051', secondary: '#E8D9B0', background: '#120E0A' },
    },
    theme: buildTheme(
      {
        primary: { main: '#3B7A2A', light: '#619A50', dark: '#295C1C', contrastText: '#FFFFFF' },
        secondary: { main: '#AD6E22', light: '#C48D48', dark: '#855312', contrastText: '#FFFFFF' },
        ...statusLight,
        background: { default: '#F1F1E8', paper: '#FBFAF3' },
        text: {
          primary: '#20241B',
          secondary: '#5D6552',
          disabled: 'rgba(32, 36, 27, 0.38)',
        },
        divider: 'rgba(45, 55, 35, 0.12)',
        action: {
          hover: 'rgba(45, 55, 35, 0.05)',
          selected: 'rgba(59, 122, 42, 0.10)',
        },
      },
      {
        primary: { main: '#4CB051', light: '#78C67C', dark: '#358B3A', contrastText: '#06230B' },
        secondary: { main: '#E8D9B0', light: '#F2E8CC', dark: '#C9B683', contrastText: '#241C08' },
        ...statusDark,
        background: { default: '#120E0A', paper: '#1C1610' },
        text: {
          primary: '#EFE8DC',
          secondary: '#B0A490',
          disabled: 'rgba(239, 232, 220, 0.38)',
        },
        divider: 'rgba(240, 230, 210, 0.09)',
        action: {
          hover: 'rgba(240, 230, 210, 0.05)',
          selected: 'rgba(76, 176, 81, 0.16)',
        },
      },
    ),
  },
];

export const DEFAULT_THEME_ID: ThemeId = 'domovoy';

export function isThemeId(value: string): value is ThemeId {
  return themeOptions.some((option) => option.id === value);
}

export function getTheme(id: string) {
  return (themeOptions.find((option) => option.id === id) ?? themeOptions[0]).theme;
}

/** Default theme — kept as the module default so existing imports keep working. */
const theme = getTheme(DEFAULT_THEME_ID);

export default theme;
