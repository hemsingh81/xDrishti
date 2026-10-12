import { Box, Button, CircularProgress, TableCell, TableRow, Typography } from '@mui/material';
import { table } from '@/theme';
import type { GridInfinite } from './types';

interface GridMoreRowProps {
  infinite: GridInfinite;
  /** Columns to span (including the checkbox column). */
  colSpan: number;
  /** `aria-rowindex` of the row: just after the loaded rows. */
  rowIndex: number;
  /** Receives the row element: the sentinel the grid observes. */
  sentinelRef: (element: HTMLElement | null) => void;
}

/**
 * The last row of an infinite list. Idle it is an invisible sentinel (the grid observes it to load the next page); while
 * loading it shows a small spinner; after a failed load it says so with a Retry button.
 */
export function GridMoreRow({ infinite, colSpan, rowIndex, sentinelRef }: GridMoreRowProps) {
  const { loadingMore, error, onRetryMore } = infinite;
  const failed = (error ?? null) !== null;
  const idle = !loadingMore && !failed;
  return (
    <TableRow ref={sentinelRef} role="row" aria-rowindex={rowIndex} aria-hidden={idle ? true : undefined}>
      <TableCell
        role="gridcell"
        colSpan={colSpan}
        sx={{ p: 0, border: 0, height: idle ? 1 : table.rowHeight, textAlign: 'center' }}
      >
        {failed && (
          <Box sx={{ display: 'inline-flex', alignItems: 'center', gap: 1 }}>
            <Typography variant="body2" color="error" title={error ?? undefined}>
              Could not load more rows
            </Typography>
            {onRetryMore && (
              <Button size="small" onClick={onRetryMore}>
                Retry
              </Button>
            )}
          </Box>
        )}
        {loadingMore && !failed && <CircularProgress size={16} aria-label="Loading more rows" />}
      </TableCell>
    </TableRow>
  );
}
