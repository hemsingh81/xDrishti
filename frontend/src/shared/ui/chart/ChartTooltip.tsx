import { Box, Paper, Stack, Typography } from '@mui/material';
import { numeric } from '@/theme';
import type { ChartTokens } from './chartTheme';
import type { ChartSeries } from './types';

interface ChartTooltipProps {
  /** The date and the value of each visible line on it (null = none that day). */
  date: string;
  rows: readonly { series: ChartSeries; value: number | null }[];
  /** The x of the date in the chart (px) and the chart's width: the box sits on the roomier side of the pointer. */
  x: number;
  width: number;
  colors: ChartTokens['colors'];
  valueFormatter: (value: number) => string;
}

/**
 * The tooltip of the chart: the date and every visible line's value, next to the dashed pointer line. Plain React over
 * the canvas, so it takes the theme's colours and the caller's formatters. It is decoration (the table has the same
 * figures), so it is hidden from assistive technology and never takes the pointer.
 */
export function ChartTooltip({ date, rows, x, width, colors, valueFormatter }: ChartTooltipProps) {
  const left = x > width / 2;
  return (
    <Paper
      aria-hidden
      elevation={3}
      sx={{
        position: 'absolute',
        top: 8,
        left: x,
        transform: left ? 'translateX(calc(-100% - 12px))' : 'translateX(12px)',
        px: 1.25,
        py: 0.75,
        minWidth: 160,
        pointerEvents: 'none',
        zIndex: 1,
        border: 1,
        borderColor: 'divider',
      }}
    >
      <Typography variant="caption" sx={{ fontWeight: 600, display: 'block', mb: 0.25 }}>
        {date}
      </Typography>
      {rows.map(({ series, value }) => (
        <Stack key={series.id} direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', gap: 2 }}>
          <Stack direction="row" sx={{ alignItems: 'center', gap: 0.75 }}>
            <Box
              component="span"
              sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: colors[series.color], flex: 'none' }}
            />
            <Typography variant="caption">{series.name}</Typography>
          </Stack>
          <Typography variant="caption" sx={{ fontWeight: 600, ...numeric }}>
            {value === null ? '—' : valueFormatter(value)}
          </Typography>
        </Stack>
      ))}
    </Paper>
  );
}
