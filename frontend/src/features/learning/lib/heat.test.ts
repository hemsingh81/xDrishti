import { describe, expect, it } from 'vitest';
import { blend, contrast } from '@/test/colour';
import { palettes, type ColorMode } from '@/theme';
import { heat, tintAlpha } from './cells';

// The heatmap adds no token: tiles are tints of the existing gain / loss colours over the card, and hatching is the secondary
// text colour at a low alpha over the page background. These are the contrasts of the words printed inside them.
describe.each<ColorMode>(['light', 'dark'])('heatmap tile colours, %s', (mode) => {
  const p = palettes[mode];

  it.each(['good', 'bad'] as const)('text is at least 4.5:1 on the strongest %s tint', (key) => {
    const tint = blend(p[key], p.surface, heat.tintMax);
    expect(contrast(p.text, tint)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(['good', 'bad'] as const)(
    'the weakest %s tint is still a tint of the card, and text on it is at least 4.5:1',
    (key) => {
      const tint = blend(p[key], p.surface, heat.tintMin);
      expect(contrast(p.text, tint)).toBeGreaterThanOrEqual(4.5);
      expect(contrast(p.textSecondary, tint)).toBeGreaterThanOrEqual(4.5);
    },
  );

  it('text and secondary text are at least 4.5:1 on the hatching stripe and on the tile behind it', () => {
    const stripe = blend(p.textSecondary, p.background, heat.hatchAlpha);
    for (const ground of [stripe, p.background]) {
      expect(contrast(p.text, ground)).toBeGreaterThanOrEqual(4.5);
      expect(contrast(p.textSecondary, ground)).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('the hatching stripe can be told from the tile behind it (the words carry the meaning, the pattern helps)', () => {
    const stripe = blend(p.textSecondary, p.background, heat.hatchAlpha);
    expect(contrast(stripe, p.background)).toBeGreaterThanOrEqual(1.1);
  });
});

describe('tint strength', () => {
  it('grows with the size of the expectancy, symmetric for loss and gain, and stays within its bounds', () => {
    expect(tintAlpha(0.01)).toBeGreaterThanOrEqual(heat.tintMin);
    expect(tintAlpha(0.25)).toBeGreaterThan(tintAlpha(0.1));
    expect(tintAlpha(-0.25)).toBe(tintAlpha(0.25));
    expect(tintAlpha(5)).toBe(heat.tintMax);
  });
});
