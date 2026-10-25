import { Box, Button } from '@mui/material';
import { lazy, Suspense, useId, useState } from 'react';
import { LoadingState } from '@/shared/ui';

// The chart (and its query) is its own chunk, loaded only when the owner expands it.
const TicketChart = lazy(() => import('./TicketChart'));

/** A "Chart" disclosure on a ticket card: collapsed until asked (on a phone too), then the lazy daily chart. */
export function TicketChartPanel({ planId, itemId, symbol }: { planId: number; itemId: number; symbol: string }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  return (
    <Box>
      <Button
        size="small"
        aria-expanded={open}
        aria-controls={id}
        onClick={() => {
          setOpen((v) => !v);
        }}
      >
        {open ? `Hide chart of ${symbol}` : `Chart of ${symbol}`}
      </Button>
      {open && (
        <Box id={id}>
          <Suspense fallback={<LoadingState label="Loading chart…" />}>
            <TicketChart planId={planId} itemId={itemId} />
          </Suspense>
        </Box>
      )}
    </Box>
  );
}
