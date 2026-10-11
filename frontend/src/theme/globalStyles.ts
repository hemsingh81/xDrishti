import type { Theme } from '@mui/material/styles';
import { focusRing, monoFontFamily, numeric } from './tokens';

/** Elements that take keyboard focus; they all get the same ring. */
const interactive = [
  'a[href]',
  'button',
  'input',
  'select',
  'textarea',
  'summary',
  '[tabindex]:not([tabindex="-1"])',
  ...['button', 'menuitem', 'option', 'tab', 'switch', 'checkbox', 'radio'].map((r) => `[role="${r}"]`),
].join(', ');
/** Items packed edge to edge in menus, lists and tab strips: the ring goes inside so it is not clipped by the container. */
const packed = '[role="menuitem"], [role="option"], [role="tab"], .MuiListItemButton-root, .MuiMenuItem-root';

/** CssBaseline overrides: focus ring, reduced motion, antialiasing, body font features, `.numeric` / `.mono`. */
export function createGlobalStyles(theme: Theme) {
  const ring = `${focusRing.width}px solid ${theme.palette.primary.main}`;
  return {
    html: { WebkitTextSizeAdjust: '100%' },
    body: {
      WebkitFontSmoothing: 'antialiased',
      MozOsxFontSmoothing: 'grayscale',
      textRendering: 'optimizeLegibility',
      fontFeatureSettings: "'cv11', 'ss03'",
    },
    // `:is()` takes the specificity of its most specific argument, so this beats MUI's `outline: 0` on ButtonBase.
    [`:is(${interactive}):focus-visible`]: { outline: ring, outlineOffset: focusRing.offset },
    [`:is(${packed}):focus-visible`]: { outlineOffset: -focusRing.width },
    // Text fields and selects show focus on their own border (a 2 px accent border), so the inner input / select box
    // gets no extra ring — it would draw a second border over the floating label. The doubled class makes this rule at
    // least as specific as the `:is()` ring above (whose specificity is that of `[tabindex]:not([tabindex])`), and it
    // comes later, so it wins.
    '.MuiInputBase-root .MuiInputBase-input:focus-visible, .MuiInputBase-root .MuiSelect-select:focus-visible': {
      outline: 'none',
    },
    // Numeric roles: aligned digits, slashed zero.
    '.numeric': { ...numeric },
    '.mono': { fontFamily: monoFontFamily },
    '@media (prefers-reduced-motion: reduce)': {
      '*, *::before, *::after': {
        animationDuration: '0.01ms !important',
        animationIterationCount: '1 !important',
        transitionDuration: '0.01ms !important',
        scrollBehavior: 'auto !important',
      },
    },
  };
}
