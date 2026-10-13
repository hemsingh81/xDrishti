import { Box, Button, Stack } from '@mui/material';
import { useId } from 'react';

export interface CategoryLegendEntry {
  id: string;
  name: string;
  /** Slot of the categorical palette. */
  color: number;
  /** A figure after the name ("32.1%"). */
  detail?: string;
}

interface CategoryLegendProps {
  /** Accessible name of the group ("Slices shown"). */
  label: string;
  entries: readonly CategoryLegendEntry[];
  hidden: ReadonlySet<string>;
  /** The colours of the active theme, indexed by slot. */
  colors: readonly string[];
  onToggle: (id: string) => void;
}

const lastReason = 'At least one must stay visible';

/**
 * The legend: one toggle button per slice or stack, outside the canvas so the keyboard and screen readers reach it. A
 * hidden one shows as not pressed and struck through; the last visible one cannot be hidden. The swatch is a colour, the
 * name and figure beside it say the same in words.
 */
export function CategoryLegend({ label, entries, hidden, colors, onToggle }: CategoryLegendProps) {
  const visibleCount = entries.filter((e) => !hidden.has(e.id)).length;
  const reasonId = useId();
  return (
    <Stack direction="row" role="group" aria-label={label} sx={{ flexWrap: 'wrap', gap: 0.75 }}>
      {entries.map((entry) => {
        const on = !hidden.has(entry.id);
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
              onToggle(entry.id);
            }}
            startIcon={
              <Box
                component="span"
                aria-hidden
                sx={{ width: 10, height: 10, borderRadius: '2px', flex: 'none', bgcolor: colors[entry.color] }}
              />
            }
            sx={{
              textDecoration: on ? 'none' : 'line-through',
              color: on ? 'text.primary' : 'text.secondary',
              borderColor: 'divider',
              '&.Mui-disabled': { color: 'text.primary', borderColor: 'divider' },
              '& .MuiButton-startIcon': { mr: 0.75 },
            }}
          >
            <span>
              {entry.name}
              {entry.detail !== undefined && (
                <>
                  {' '}
                  <Box component="span" sx={{ color: 'text.secondary', fontWeight: 400 }}>
                    {entry.detail}
                  </Box>
                </>
              )}
            </span>
          </Button>
        );
        // A disabled button gets no pointer events: the span carries the tooltip, the description the screen reader.
        return (
          <Box key={entry.id} component="span" title={last ? lastReason : undefined}>
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
          {lastReason}
        </Box>
      )}
    </Stack>
  );
}
