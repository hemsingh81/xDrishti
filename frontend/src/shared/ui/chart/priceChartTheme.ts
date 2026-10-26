import type { Theme } from '@mui/material/styles';
import type { PriceChartColors } from './priceChartTypes';

/** The price chart's colours from the theme tokens (light and dark): grid, text, up, down, stop and target lines. */
export function priceChartColors(theme: Theme): PriceChartColors {
  const { palette } = theme;
  return {
    background: palette.background.paper,
    text: palette.text.secondary,
    grid: palette.divider,
    border: palette.divider,
    up: palette.success.main,
    down: palette.error.main,
    stop: palette.error.main,
    target: palette.success.main,
    entry: palette.info.main,
  };
}
