import { describe, expect, it } from 'vitest';
import { contrast } from '@/test/colour';
import { createAppTheme, type ColorMode } from '@/theme';
import { priceChartColors } from './priceChartTheme';

describe.each<ColorMode>(['light', 'dark'])('price chart colours, %s mode', (mode) => {
  const colors = priceChartColors(createAppTheme(mode));

  it('come from the theme and differ from each other where meaning differs', () => {
    expect(colors.up).not.toBe(colors.down);
    expect(colors.entry).not.toBe(colors.stop);
    expect(colors.target).toBe(colors.up);
    expect(colors.stop).toBe(colors.down);
  });

  it('text is at least 4.5:1 and the candle and level colours at least 3:1 on the chart background', () => {
    expect(contrast(colors.text, colors.background)).toBeGreaterThanOrEqual(4.5);
    for (const key of ['up', 'down', 'stop', 'target', 'entry'] as const) {
      expect(contrast(colors[key], colors.background)).toBeGreaterThanOrEqual(3);
    }
  });
});
