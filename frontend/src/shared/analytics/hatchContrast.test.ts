import { describe, expect, it } from 'vitest';
import { blend, contrast } from '@/test/colour';
import { palettes, type ColorMode } from '@/theme/tokens';
import { HATCH_ALPHA } from './HeatGrid';

// The hatch stripe (secondary text at HATCH_ALPHA over the page background) sits under the words of an insufficient cell.
describe.each<ColorMode>(['light', 'dark'])('%s hatched cell', (mode) => {
  const p = palettes[mode];
  it('keeps text and secondary text at 4.5:1 on the stripe', () => {
    const stripe = blend(p.textSecondary, p.background, HATCH_ALPHA);
    expect(contrast(p.text, stripe)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(p.textSecondary, stripe)).toBeGreaterThanOrEqual(4.5);
  });
});
