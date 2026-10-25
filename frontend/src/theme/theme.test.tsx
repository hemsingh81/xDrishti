import {
  Alert,
  Button,
  Card,
  Chip,
  CssBaseline,
  IconButton,
  ListItemButton,
  Table,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  ThemeProvider,
  Typography,
} from '@mui/material';
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { createAppTheme } from './theme';
import { controlHeight, elevation, fontFamily, palettes, typeScale, type ColorMode } from './tokens';

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

  it('renders chips as tonal pills (soft tint, coloured text) whether outlined or filled', () => {
    renderThemed(
      mode,
      <>
        <Chip variant="outlined" label="Built-in" />
        <Chip variant="outlined" color="success" label="Tracked" />
        <Chip color="warning" label="Default date" />
      </>,
    );
    const plain = getComputedStyle(screen.getByText('Built-in').parentElement!);
    expect(plain.backgroundColor).toBe(rgb(p.surfaceStrong));
    expect(plain.color).toBe(rgb(p.textSecondary));
    expect(plain.borderRadius).toBe('999px');
    const success = getComputedStyle(screen.getByText('Tracked').parentElement!);
    expect(success.backgroundColor).toBe(rgb(p.goodSoft));
    expect(success.color).toBe(rgb(p.good));
    const warning = getComputedStyle(screen.getByText('Default date').parentElement!);
    expect(warning.backgroundColor).toBe(rgb(p.warnSoft));
    expect(warning.color).toBe(rgb(p.warn));
  });

  it('draws a border in the chip colour on a pressed toggle chip, not only a different tint', () => {
    renderThemed(
      mode,
      <>
        <Chip clickable aria-pressed label="Pressed" color="primary" variant="filled" />
        <Chip clickable aria-pressed={false} label="Released" color="default" variant="outlined" />
      </>,
    );
    const pressed = getComputedStyle(screen.getByRole('button', { name: 'Pressed' }));
    expect(pressed.borderTopWidth).toBe('1px');
    expect(pressed.borderTopColor).toBe(rgb(p.accent));
    const released = getComputedStyle(screen.getByRole('button', { name: 'Released' }));
    expect(released.borderTopColor).toBe('rgba(0, 0, 0, 0)');
  });

  it('renders the selected (active) list item on accentSoft', () => {
    renderThemed(mode, <ListItemButton className="active">Accounts</ListItemButton>);
    const item = screen.getByRole('button', { name: 'Accounts' });
    expect(getComputedStyle(item).backgroundColor).toBe(rgb(p.accentSoft));
    expect(injectedCss()).toContain('::before');
  });

  it('sizes buttons and icon buttons like the prototype: 36 px (small 30, large 44), a 10 px radius', () => {
    renderThemed(
      mode,
      <>
        <Button>Medium</Button>
        <Button size="small">Small</Button>
        <Button size="large">Large</Button>
        <IconButton aria-label="Close" />
      </>,
    );
    expect(getComputedStyle(screen.getByRole('button', { name: 'Medium' })).minHeight).toBe(
      `${controlHeight.medium}px`,
    );
    expect(getComputedStyle(screen.getByRole('button', { name: 'Small' })).minHeight).toBe(`${controlHeight.small}px`);
    expect(getComputedStyle(screen.getByRole('button', { name: 'Large' })).minHeight).toBe(`${controlHeight.large}px`);
    expect(getComputedStyle(screen.getByRole('button', { name: 'Medium' })).borderRadius).toBe('10px');
    const icon = getComputedStyle(screen.getByRole('button', { name: 'Close' }));
    expect(icon.padding).toBe('6px');
    expect(icon.borderRadius).toBe('10px');
  });

  it('makes every text field 36 px tall (small by default) with a 10 px radius', () => {
    renderThemed(mode, <TextField label="Name" />);
    const input = screen.getByLabelText('Name');
    expect(input.closest('.MuiInputBase-root')).toHaveClass('MuiInputBase-sizeSmall');
    expect(getComputedStyle(input.closest('.MuiInputBase-root')!).minHeight).toBe(`${controlHeight.medium}px`);
    expect(getComputedStyle(input.closest('.MuiInputBase-root')!).borderRadius).toBe('10px');
  });

  it('renders cards with a 12 px radius and the soft prototype shadow', () => {
    renderThemed(mode, <Card data-testid="card">x</Card>);
    const card = getComputedStyle(screen.getByTestId('card'));
    expect(card.borderRadius).toBe('12px');
    expect(card.boxShadow.replace(/,\s*/g, ', ')).toBe(elevation[mode].md);
  });

  it('renders a banner (alert) on its soft tint with a matching hairline and 13 px text', () => {
    renderThemed(
      mode,
      <>
        <Alert severity="warning">Heads up</Alert>
        <Alert severity="info">Note</Alert>
      </>,
    );
    const warning = getComputedStyle(screen.getByText('Heads up').closest('[role="alert"]')!);
    expect(warning.backgroundColor).toBe(rgb(p.warnSoft));
    expect(warning.padding).toBe('11px 15px');
    expect(warning.borderRadius).toBe('12px');
    expect(warning.color).toBe(rgb(p.text));
    expect(getComputedStyle(screen.getByText('Note').closest('[role="alert"]')!).backgroundColor).toBe(
      rgb(p.accentSoft),
    );
  });

  it('styles table headers as small uppercase caps with 9 / 12 px cell padding', () => {
    renderThemed(
      mode,
      <Table>
        <TableHead>
          <TableRow>
            <TableCell>Symbol</TableCell>
          </TableRow>
        </TableHead>
      </Table>,
    );
    const th = getComputedStyle(screen.getByRole('columnheader', { name: 'Symbol' }));
    expect(th.textTransform).toBe('uppercase');
    expect(th.fontWeight).toBe('650');
    expect(th.padding).toBe('9px 12px');
    expect(th.backgroundColor).toBe(rgb(p.surfaceAlt));
  });

  it('reads dotted palette paths in the Typography colour prop (secondary text is secondary)', () => {
    renderThemed(
      mode,
      <>
        <Typography color="text.secondary">Hint</Typography>
        <Typography color="error.main">Oops</Typography>
        <Typography color="text.tertiary">Quiet</Typography>
      </>,
    );
    expect(getComputedStyle(screen.getByText('Hint')).color).toBe(rgb(p.textSecondary));
    expect(getComputedStyle(screen.getByText('Oops')).color).toBe(rgb(p.bad));
    expect(getComputedStyle(screen.getByText('Quiet')).color).toBe(rgb(p.textTertiary));
  });

  it('sets the root font to 14 px so rem-based sizes match the prototype', () => {
    renderThemed(mode, <span>x</span>);
    expect(injectedCss()).toContain('html{-webkit-text-size-adjust:100%;font-size:87.5%');
    const theme = createAppTheme(mode);
    expect(theme.typography.htmlFontSize).toBe(14);
    expect(theme.typography.pxToRem(24)).toBe(`${24 / 14}rem`);
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

  it('grows the root font on large monitors (15 px from 1680, 16 px from 2560) and sets 44 px targets on phones', () => {
    renderThemed(mode, <span>x</span>);
    const css = injectedCss();
    expect(css).toMatch(/@media\(min-width:1680px\)\{html\{font-size:93\.75%/);
    expect(css).toMatch(/@media\(min-width:2560px\)\{html\{font-size:100%/);
    expect(css).toMatch(/@media\(max-width:479\.95px\)\{[^}]*\.MuiButton-root[^}]*\{min-height:44px;min-width:44px/);
  });
});
