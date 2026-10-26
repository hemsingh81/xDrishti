import { alpha, type Theme } from '@mui/material/styles';
import {
  focusRing,
  layout,
  monoFontFamily,
  numeric,
  pagePadding,
  palettes,
  rootFontSize,
  rootFontSizeLarge,
  touchTarget,
} from './tokens';

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

/** The controls that reach 44 px on phones, and those left at their dense size. */
export const touchControls = '.MuiButton-root, .MuiIconButton-root, .MuiTab-root, .MuiChip-clickable, [role="button"]';
export const touchExempt = [
  '.MuiButton-sizeSmall',
  '.MuiIconButton-sizeSmall',
  '.MuiChip-sizeSmall',
  '[data-dense]',
  ...['.MuiTableCell-root', '.MuiInputBase-root', '.MuiAutocomplete-root', '[data-dense]'].map((c) => `${c} *`),
].join(', ');

/** CssBaseline overrides: focus ring, reduced motion, antialiasing, body font features, `.numeric` / `.mono`. */
export function createGlobalStyles(theme: Theme) {
  const ring = `${focusRing.width}px solid ${theme.palette.primary.main}`;
  return {
    // The prototype's body text is 14 px; every rem in the theme is relative to it. A percentage keeps the browser's text-size setting.
    // Doc 13 §1d: the base grows on large monitors (15 px from 1680, 16 px from 2560); every rem size follows.
    html: {
      WebkitTextSizeAdjust: '100%',
      fontSize: `${(rootFontSize / 16) * 100}%`,
      // A focused element is never hidden under the sticky top bar (WCAG 2.4.11).
      scrollPaddingTop: `${layout.topBarHeight + 16}px`,
    },
    [`@media (min-width:${pagePadding.wide.minWidth}px)`]: {
      html: { fontSize: `${(rootFontSizeLarge.wide / 16) * 100}%` },
    },
    [`@media (min-width:${pagePadding.ultra.minWidth}px)`]: {
      html: { fontSize: `${(rootFontSizeLarge.ultra / 16) * 100}%` },
    },
    // Phones: controls are at least 44 px (doc 13 §1d). The `:not()` list doubles the specificity so the rule beats the
    // components' own single-class rules whatever the insertion order, and exempts the dense controls: small buttons,
    // icon buttons and chips, anything inside a table cell, a text field or an autocomplete, and `data-dense`. Inline
    // links are not listed.
    [`@media (max-width:${touchTarget.phoneMaxWidth}px)`]: {
      [`:is(${touchControls}):not(${touchExempt})`]: { minHeight: touchTarget.phone, minWidth: touchTarget.phone },
    },
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
    // The prototype's thin scrollbars and soft text selection.
    '*': { scrollbarWidth: 'thin', scrollbarColor: `${palettes[theme.palette.mode].borderStrong} transparent` },
    '::selection': { backgroundColor: alpha(theme.palette.primary.main, 0.25) },
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
