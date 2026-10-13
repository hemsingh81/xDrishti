import type { Theme } from '@mui/material/styles';
import type { ChartColor } from './types';

/** Everything the chart paints with, taken from the MUI theme at run time (so light and dark both work). */
export interface ChartTokens {
  colors: Record<ChartColor, string>;
  text: string;
  textSecondary: string;
  /** Axis lines and the tooltip frame. */
  border: string;
  /** Grid lines. */
  grid: string;
  surface: string;
  fontFamily: string;
}

export function chartTokens(theme: Theme): ChartTokens {
  const { palette } = theme;
  return {
    colors: {
      accent: palette.primary.main,
      neutral: palette.text.secondary,
      info: palette.info.main,
      good: palette.success.main,
      warn: palette.warning.main,
      bad: palette.error.main,
    },
    text: palette.text.primary,
    textSecondary: palette.text.secondary,
    border: palette.divider,
    grid: palette.divider,
    surface: palette.background.paper,
    fontFamily: theme.typography.fontFamily ?? 'sans-serif',
  };
}
