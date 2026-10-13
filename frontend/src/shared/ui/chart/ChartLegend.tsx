import { Box, Button, Stack } from '@mui/material';
import { useId } from 'react';
import type { ChartTokens } from './chartTheme';
import type { ChartDash, ChartSeries } from './types';

const dashes: Record<ChartDash, string | undefined> = { solid: undefined, dashed: '6 4', dotted: '2 3' };

/** A short piece of the line itself (colour and style), so the legend matches what is drawn. */
function Swatch({ color, dash = 'solid' }: { color: string; dash?: ChartDash }) {
  return (
    <Box component="svg" width={24} height={10} viewBox="0 0 24 10" aria-hidden focusable="false" sx={{ flex: 'none' }}>
      <line x1={1} x2={23} y1={5} y2={5} stroke={color} strokeWidth={2.5} strokeDasharray={dashes[dash]} />
    </Box>
  );
}

const lastLineReason = 'At least one line must stay visible';

interface ChartLegendProps {
  series: readonly ChartSeries[];
  hidden: ReadonlySet<string>;
  colors: ChartTokens['colors'];
  onToggle: (id: string) => void;
}

/**
 * The legend: one toggle button per line, outside the canvas so the keyboard and screen readers reach it. A hidden
 * line shows as not pressed and struck through; the last visible line cannot be hidden.
 */
export function ChartLegend({ series, hidden, colors, onToggle }: ChartLegendProps) {
  const visibleCount = series.filter((s) => !hidden.has(s.id)).length;
  const reasonId = useId();
  return (
    <Stack direction="row" role="group" aria-label="Lines shown" sx={{ flexWrap: 'wrap', gap: 0.75 }}>
      {series.map((s) => {
        const on = !hidden.has(s.id);
        const last = on && visibleCount === 1;
        const button = (
          <Button
            size="small"
            variant={on ? 'outlined' : 'text'}
            color="inherit"
            aria-pressed={on}
            aria-describedby={last ? reasonId : undefined}
            disabled={last}
            onClick={() => {
              onToggle(s.id);
            }}
            startIcon={<Swatch color={colors[s.color]} dash={s.dash} />}
            sx={{
              textDecoration: on ? 'none' : 'line-through',
              color: on ? 'text.primary' : 'text.secondary',
              borderColor: 'divider',
              '&.Mui-disabled': { color: 'text.primary', borderColor: 'divider' },
              '& .MuiButton-startIcon': { mr: 0.75 },
            }}
          >
            {s.name}
          </Button>
        );
        // A disabled button gets no pointer events: the span carries the tooltip, the description the screen reader.
        return last ? (
          <Box key={s.id} component="span" title={lastLineReason}>
            {button}
          </Box>
        ) : (
          <Box key={s.id} component="span">
            {button}
          </Box>
        );
      })}
      {visibleCount === 1 && (
        <Box
          component="span"
          id={reasonId}
          sx={{
            position: 'absolute',
            width: 1,
            height: 1,
            overflow: 'hidden',
            clip: 'rect(0 0 0 0)',
            whiteSpace: 'nowrap',
          }}
        >
          {lastLineReason}
        </Box>
      )}
    </Stack>
  );
}
