import { createTheme, type Theme } from '@mui/material/styles';
import { fontFamily, palettes, shape, type ColorMode } from './tokens';

/** Builds the MUI theme for a colour mode. Memoise the result — themes are expensive to create. */
export function createAppTheme(mode: ColorMode): Theme {
  const p = palettes[mode];
  return createTheme({
    palette: {
      mode,
      primary: { main: p.accent },
      success: { main: p.good },
      error: { main: p.bad },
      warning: { main: p.warn },
      info: { main: p.info },
      background: { default: p.background, paper: p.surface },
      text: { primary: p.text, secondary: p.textSecondary },
      divider: p.border,
    },
    shape: { borderRadius: shape.radius },
    typography: {
      fontFamily,
      fontSize: 14,
      h1: { fontSize: '1.5rem', fontWeight: 700, letterSpacing: '-0.02em' },
      h2: { fontSize: '1.125rem', fontWeight: 650 },
      h3: { fontSize: '0.95rem', fontWeight: 650 },
      button: { textTransform: 'none', fontWeight: 600 },
    },
    components: {
      MuiButton: { defaultProps: { disableElevation: true } },
      MuiCard: {
        defaultProps: { variant: 'outlined' },
        styleOverrides: { root: { borderColor: p.border } },
      },
      MuiAppBar: { defaultProps: { elevation: 0, color: 'inherit' } },
    },
  });
}
