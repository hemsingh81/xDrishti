import { Button, Chip, CssBaseline, ListItemButton, ThemeProvider } from '@mui/material';
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { createAppTheme } from './theme';
import { fontFamily, palettes, typeScale, type ColorMode } from './tokens';

function rgb(hex: string): string {
  const n = Number.parseInt(hex.slice(1), 16);
  return `rgb(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255})`;
}

/** Every rule emotion has injected so far, whitespace-stripped so assertions ignore formatting. */
function injectedCss(): string {
  return Array.from(document.querySelectorAll('style'))
    .map((s) => s.textContent)
    .join('\n')
    .replace(/\s+/g, '');
}

function renderThemed(mode: ColorMode, ui: React.ReactNode) {
  return render(
    <ThemeProvider theme={createAppTheme(mode)}>
      <CssBaseline />
      {ui}
    </ThemeProvider>,
  );
}

describe.each(['light', 'dark'] as const)('%s theme', (mode) => {
  const p = palettes[mode];

  it('maps the palette, including the accent label colour', () => {
    const theme = createAppTheme(mode);
    expect(theme.palette.primary.main).toBe(p.accent);
    expect(theme.palette.primary.contrastText).toBe(p.accentContrast);
    expect(theme.palette.divider).toBe(p.border);
  });

  it('defines every typography role from the type scale', () => {
    const theme = createAppTheme(mode);
    expect(theme.typography.fontFamily).toBe(fontFamily);
    for (const key of Object.keys(typeScale) as (keyof typeof typeScale)[]) {
      expect(theme.typography[key].fontWeight, key).toBe(typeScale[key].weight);
    }
  });

  it('renders a contained primary button in the accent with the accentContrast label, dimmed on hover', () => {
    renderThemed(mode, <Button variant="contained">Save</Button>);
    const button = screen.getByRole('button', { name: 'Save' });
    expect(button).toBeVisible();
    // MUI v9 expresses the variant colours as custom properties on the button.
    const css = injectedCss();
    expect(css).toContain(`--variant-containedBg:${p.accent}`);
    expect(css).toContain(`--variant-containedColor:${p.accentContrast}`);
    expect(css).toMatch(/:hover\{background-color:rgba\(\d+,\d+,\d+,0\.88\)/);
  });

  it('renders an outlined default chip with the hairline border colour', () => {
    renderThemed(mode, <Chip variant="outlined" label="Built-in" />);
    const chip = screen.getByText('Built-in').parentElement!;
    expect(getComputedStyle(chip).borderColor).toBe(rgb(p.border));
  });

  it('renders the selected (active) list item on accentSoft', () => {
    renderThemed(mode, <ListItemButton className="active">Accounts</ListItemButton>);
    const item = screen.getByRole('button', { name: 'Accounts' });
    expect(getComputedStyle(item).backgroundColor).toBe(rgb(p.accentSoft));
    expect(injectedCss()).toContain('::before');
  });

  it('gives `.numeric` tabular figures and a slashed zero', () => {
    renderThemed(mode, <span className="numeric">1,234.50</span>);
    const css = injectedCss();
    expect(css).toMatch(/\.numeric\{[^}]*font-variant-numeric:tabular-nums/);
    expect(css).toMatch(/\.numeric\{[^}]*'cv11','zero'/);
  });

  it('applies the focus ring to menu items, options, tabs and switches, inset in packed lists', () => {
    renderThemed(mode, <span>x</span>);
    const css = injectedCss();
    for (const role of ['menuitem', 'option', 'tab', 'switch', 'checkbox', 'radio']) {
      expect(css, role).toContain(`[role="${role}"]`);
    }
    expect(css).toContain(':focus-visible{outline:2pxsolid');
    expect(css).toMatch(/\.MuiMenuItem-root\):focus-visible\{outline-offset:-2px/);
  });

  it('keeps the ring off text-field inputs and select boxes (the field border shows focus), with enough specificity', () => {
    renderThemed(mode, <span>x</span>);
    const css = injectedCss();
    // (The helper strips whitespace, so the descendant combinator reads `.a.b` here.)
    // The global ring uses `:is()`, whose specificity is that of its strongest member (`[tabindex]:not([tabindex=...])`
    // = 2 classes/attributes); the field rule must be at least that strong (3 with `:focus-visible`) and come later,
    // otherwise a second border is drawn over the floating label.
    const rule =
      /\.MuiInputBase-root\.MuiInputBase-input:focus-visible,\.MuiInputBase-root\.MuiSelect-select:focus-visible\{outline:none/;
    expect(css).toMatch(rule);
    expect(css.search(rule)).toBeGreaterThan(css.indexOf(':focus-visible{outline:2pxsolid'));
  });

  it('disables transitions and animations under prefers-reduced-motion', () => {
    renderThemed(mode, <span>x</span>);
    expect(injectedCss()).toMatch(/prefers-reduced-motion:reduce\)\{[^}]*\{[^}]*transition-duration:0\.01ms!important/);
  });
});
