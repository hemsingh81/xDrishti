import { alpha, createTheme, type Theme, type ThemeOptions } from '@mui/material/styles';
import { createComponents } from './components';
import { createGlobalStyles } from './globalStyles';
import { elevation, fontFamily, palettes, shape, stateAlpha, typeScale, type ColorMode } from './tokens';

type Variant = keyof typeof typeScale;

function variant(key: Variant) {
  const t = typeScale[key];
  return { fontSize: `${t.size}rem`, fontWeight: t.weight, lineHeight: t.lineHeight, letterSpacing: `${t.tracking}em` };
}

/** MUI wants 25 shadows: the three elevation steps stretched over its 0-24 scale. */
function buildShadows(mode: ColorMode): ThemeOptions['shadows'] {
  const e = elevation[mode];
  const steps = ['none', ...Array.from({ length: 24 }, (_, i) => (i < 2 ? e.sm : i < 8 ? e.md : e.lg))];
  return steps as unknown as ThemeOptions['shadows'];
}

/** Builds the MUI theme for a colour mode. Memoise the result — themes are expensive to create. */
export function createAppTheme(mode: ColorMode): Theme {
  const p = palettes[mode];
  const base = createTheme({
    palette: {
      mode,
      primary: { main: p.accent, contrastText: p.accentContrast },
      success: { main: p.good },
      error: { main: p.bad },
      warning: { main: p.warn },
      info: { main: p.info },
      background: { default: p.background, paper: p.surface },
      text: { primary: p.text, secondary: p.textSecondary },
      divider: p.border,
      action: {
        hover: alpha(p.accent, 0.06),
        selected: alpha(p.accent, 0.12),
        focus: alpha(p.accent, 0.16),
        disabled: alpha(p.textSecondary, stateAlpha.disabledText),
        disabledBackground: alpha(p.textSecondary, stateAlpha.disabledFill),
      },
    },
    shape: { borderRadius: shape.radius },
    shadows: buildShadows(mode),
    typography: {
      fontFamily,
      fontSize: 14,
      h1: variant('h1'),
      h2: variant('h2'),
      h3: variant('h3'),
      h4: variant('h4'),
      h5: variant('h5'),
      h6: variant('h6'),
      subtitle1: variant('subtitle1'),
      subtitle2: variant('subtitle2'),
      body1: variant('body1'),
      body2: variant('body2'),
      caption: variant('caption'),
      overline: { ...variant('overline'), textTransform: 'uppercase' },
      button: { ...variant('button'), textTransform: 'none' },
    },
  });
  // Components and global styles read the finished palette (e.g. `primary.contrastText`), so they come second.
  return createTheme(base, {
    components: {
      ...createComponents(base, mode),
      MuiCssBaseline: { styleOverrides: createGlobalStyles(base) },
    },
  });
}
