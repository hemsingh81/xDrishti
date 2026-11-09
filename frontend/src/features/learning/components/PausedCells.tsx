import { Box, Button, Typography } from '@mui/material';
import { useQueries } from '@tanstack/react-query';
import { cellStateQuery } from '../api/learningQueries';

interface Props {
  /** The cells named by applied or dismissed proposals; only those whose own state reads Paused get a button. */
  cells: string[];
  ask: (key: string) => () => void;
}

/** The Re-enable buttons: one per cell that is paused now (read from the cell's replayed lifecycle state, cached when already loaded). */
export function PausedCells({ cells, ask }: Props) {
  const states = useQueries({ queries: cells.map((key) => cellStateQuery(key)) });
  if (cells.length === 0) return null;
  const paused = cells.filter((_, i) => states[i]?.data?.state === 'paused');
  const loading = states.some((s) => s.isPending);
  const failed = states.some((s) => s.isError);
  return (
    <Box component="section" aria-label="Paused cells" sx={{ display: 'grid', gap: 1 }}>
      <Typography variant="body2">
        Cells that are paused now (named by an applied or dismissed proposal). Re-enabling moves a paused cell back to
        Paper; it does not make it Active and places no order.
      </Typography>
      {loading && <Typography variant="body2">Reading the state of the cells…</Typography>}
      {failed && (
        <Typography variant="body2" role="alert">
          The state of some cells could not be read, so they are not offered here.
        </Typography>
      )}
      {!loading && !failed && paused.length === 0 && <Typography variant="body2">No cell is paused.</Typography>}
      {paused.map((key) => (
        <div key={key}>
          <Button size="small" variant="outlined" onClick={ask(key)}>
            {`Re-enable cell ${key}`}
          </Button>
        </div>
      ))}
    </Box>
  );
}
