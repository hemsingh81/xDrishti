import { describe, expect, it } from 'vitest';
import {
  categorical,
  categoricalSeries,
  elevation,
  iconChip,
  iconHues,
  palettes,
  stateAlpha,
  table,
  type ColorMode,
} from './tokens';

function channel(v: number): number {
  const c = v / 255;
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function luminance(hex: string): number {
  const n = Number.parseInt(hex.slice(1), 16);
  return 0.2126 * channel((n >> 16) & 255) + 0.7152 * channel((n >> 8) & 255) + 0.0722 * channel(n & 255);
}

/** `fg` at `alpha` painted over `bg`, as #rrggbb. */
function blend(fg: string, bg: string, alpha: number): string {
  const f = Number.parseInt(fg.slice(1), 16);
  const b = Number.parseInt(bg.slice(1), 16);
  const mix = (shift: number) =>
    Math.round(((f >> shift) & 255) * alpha + ((b >> shift) & 255) * (1 - alpha))
      .toString(16)
      .padStart(2, '0');
  return `#${mix(16)}${mix(8)}${mix(0)}`;
}

/** WCAG 2.x contrast ratio between two #rrggbb colours. */
export function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (hi + 0.05) / (lo + 0.05);
}

type Key = keyof (typeof palettes)['light'];
const surfaces: Key[] = ['surface', 'background', 'surfaceAlt'];

describe.each<ColorMode>(['light', 'dark'])('%s palette contrast', (mode) => {
  const p = palettes[mode];

  it.each(surfaces)('text is at least 4.5:1 on %s', (surface) => {
    expect(contrast(p.text, p[surface])).toBeGreaterThanOrEqual(4.5);
  });

  it.each(surfaces)('secondary text is at least 4.5:1 on %s', (surface) => {
    expect(contrast(p.textSecondary, p[surface])).toBeGreaterThanOrEqual(4.5);
  });

  it('secondary text is at least 4.5:1 on the accentSoft dialog header', () => {
    expect(contrast(p.textSecondary, p.accentSoft)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(p.text, p.accentSoft)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(['accent', 'good', 'bad', 'warn', 'info'] as const)('%s text is at least 4.5:1 on the surface', (key) => {
    expect(contrast(p[key], p.surface)).toBeGreaterThanOrEqual(4.5);
  });

  it('accent text is at least 4.5:1 on accentSoft (selected rows, nav)', () => {
    expect(contrast(p.accent, p.accentSoft)).toBeGreaterThanOrEqual(4.5);
  });

  it('label on a filled accent button is at least 4.5:1', () => {
    expect(contrast(p.accentContrast, p.accent)).toBeGreaterThanOrEqual(4.5);
  });

  it('UI parts (focus ring, filled accent) are at least 3:1 against the surface and background', () => {
    expect(contrast(p.accent, p.surface)).toBeGreaterThanOrEqual(3);
    expect(contrast(p.accent, p.background)).toBeGreaterThanOrEqual(3);
  });

  it('semantic colours are at least 3:1 on the canvas (icons, markers)', () => {
    for (const key of ['good', 'bad', 'warn', 'info'] as const) {
      expect(contrast(p[key], p.background)).toBeGreaterThanOrEqual(3);
    }
  });

  it.each(['good', 'bad', 'warn', 'info'] as const)('%s text is at least 4.5:1 on accentSoft and surfaceAlt', (key) => {
    expect(contrast(p[key], p.accentSoft)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(p[key], p.surfaceAlt)).toBeGreaterThanOrEqual(4.5);
  });

  it('text is at least 4.5:1 on hovered and selected table rows', () => {
    for (const a of [table.hoverAlpha, table.selectedAlpha]) {
      const row = blend(p.accent, p.surface, a);
      expect(contrast(p.text, row)).toBeGreaterThanOrEqual(4.5);
      expect(contrast(p.textSecondary, row)).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('placeholder text, as rendered (alpha-blended), is at least 4.5:1 on the surface and surfaceAlt', () => {
    for (const bg of [p.surface, p.surfaceAlt]) {
      expect(contrast(blend(p.textSecondary, bg, stateAlpha.placeholder), bg)).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('tooltip text (background on text) is at least 4.5:1', () => {
    expect(contrast(p.background, p.text)).toBeGreaterThanOrEqual(4.5);
  });

  it('disabled text stays legible (at least 3:1) on the surface and on the disabled fill', () => {
    const fill = blend(p.textSecondary, p.surface, stateAlpha.disabledFill);
    expect(contrast(blend(p.textSecondary, p.surface, stateAlpha.disabledText), p.surface)).toBeGreaterThanOrEqual(3);
    expect(contrast(blend(p.textSecondary, fill, stateAlpha.disabledText), fill)).toBeGreaterThanOrEqual(3);
  });

  it.each(['surface', 'surfaceAlt', 'background'] as const)(
    'input border, checkbox box and switch track are at least 3:1 on %s',
    (bg) => {
      expect(contrast(p.inputBorder, p[bg])).toBeGreaterThanOrEqual(3);
    },
  );

  it('the switch thumb (surface) is at least 3:1 on the off track', () => {
    expect(contrast(p.surface, p.inputBorder)).toBeGreaterThanOrEqual(3);
  });

  it.each(['surface', 'surfaceAlt', 'background', 'accentSoft'] as const)(
    'focus ring (accent) is at least 3:1 on %s',
    (bg) => {
      expect(contrast(p.accent, p[bg])).toBeGreaterThanOrEqual(3);
    },
  );

  it('hairline borders stay quiet (below the input border)', () => {
    expect(contrast(p.border, p.surface)).toBeLessThan(contrast(p.inputBorder, p.surface));
  });
});

describe.each<ColorMode>(['light', 'dark'])('%s icon hue contrast', (mode) => {
  const p = palettes[mode];
  const tint = mode === 'dark' ? iconChip.tintDark : iconChip.tintLight;
  const pairs = Object.entries(iconHues).map(
    ([name, hue]) => [name, mode === 'dark' ? hue.light : hue.strong] as const,
  );

  // Target 3:1 for every hue (adjusted values are recorded in tokens.ts); no recorded exceptions.
  it.each(pairs)('%s icon is at least 3:1 on its chip over the surface', (_name, color) => {
    expect(contrast(color, blend(color, p.surface, tint))).toBeGreaterThanOrEqual(3);
  });

  it.each(pairs)('%s icon is at least 4:1 in dark mode (light values on a dark chip)', (_name, color) => {
    if (mode === 'dark') expect(contrast(color, blend(color, p.surface, tint))).toBeGreaterThanOrEqual(4);
  });

  it.each(pairs)('%s icon is at least 2.4:1 on its chip over the selected row (accentSoft)', (_name, color) => {
    expect(contrast(color, blend(color, p.accentSoft, tint))).toBeGreaterThanOrEqual(2.4);
  });
});

describe.each<ColorMode>(['light', 'dark'])('%s categorical palette', (mode) => {
  const p = palettes[mode];
  const colours = categorical[mode];

  it('has eight series colours and a ninth for "Other"', () => {
    expect(colours).toHaveLength(categoricalSeries + 1);
    expect(new Set(colours).size).toBe(colours.length);
  });

  it.each(colours.map((colour, index) => [index, colour] as const))(
    'colour %i (%s) is at least 3:1 on the card (surface), the same as the series colours of the charts',
    (_index, colour) => {
      expect(contrast(colour, p.surface)).toBeGreaterThanOrEqual(3);
    },
  );

  it('stays at least 3:1 on the page background and on surfaceAlt (a chart in a tinted card)', () => {
    for (const colour of colours) {
      expect(contrast(colour, p.background)).toBeGreaterThanOrEqual(3);
      expect(contrast(colour, p.surfaceAlt)).toBeGreaterThanOrEqual(3);
    }
  });
});

describe('token values from the spec', () => {
  it('has the documented palettes', () => {
    expect(palettes.light).toMatchObject({
      background: '#f4f6fa',
      surface: '#ffffff',
      surfaceAlt: '#f1f4f9',
      border: '#dfe3ea',
      inputBorder: '#7b8798',
      text: '#0f172a',
      textSecondary: '#475569',
      accent: '#4338ca',
      accentContrast: '#ffffff',
      accentSoft: '#eceefe',
      good: '#146c36',
      bad: '#b91c1c',
      warn: '#a35200',
      info: '#0e7490',
    });
    expect(palettes.dark).toMatchObject({
      background: '#0b1220',
      surface: '#131c2e',
      surfaceAlt: '#182238',
      border: '#26324a',
      inputBorder: '#5f6f8a',
      text: '#e6eaf2',
      textSecondary: '#9aa7bd',
      accent: '#9aa5ff',
      accentContrast: '#0b1220',
      accentSoft: '#222a52',
      good: '#4ade80',
      bad: '#f87171',
      warn: '#fbbf24',
      info: '#67e8f9',
    });
  });

  it('defines elevation steps for both modes', () => {
    for (const mode of ['light', 'dark'] as const) {
      expect(Object.keys(elevation[mode])).toEqual(['sm', 'md', 'lg']);
    }
  });
});
