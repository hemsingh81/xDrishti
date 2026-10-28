import { Button, Typography } from '@mui/material';
import { useState } from 'react';
import type { LiveExecutionsPage } from '../api/executionQueries';
import { figure, referenceWords } from '../lib/executionLabels';
import { WaterfallRows } from './WaterfallRows';

/** The latest live entries against their reference: total slippage split into the delay before the order and the execution itself. A row with a closed, graded round trip opens its waterfall. */
export function ExecutionTrades({ page }: { page: LiveExecutionsPage }) {
  const [open, setOpen] = useState<number | null>(null);
  if (page.items.length === 0) return <Typography>No live entry has been measured yet.</Typography>;
  return (
    <section aria-label="Measured entries">
      <Typography variant="h3" component="h3">
        Your entries against the plan
      </Typography>
      <Typography variant="body2" color="text.secondary">
        {`Latest ${String(page.items.length)} of ${String(page.total)}. Positive is worse for you; delay plus execution is the total.`}
      </Typography>
      <table aria-label="Measured entries table">
        <thead>
          <tr>
            <th scope="col">Day</th>
            <th scope="col">Symbol</th>
            <th scope="col">Bucket</th>
            <th scope="col">Reference</th>
            <th scope="col">Total (bp)</th>
            <th scope="col">Total (ticks)</th>
            <th scope="col">Total (R)</th>
            <th scope="col">Delay (R)</th>
            <th scope="col">Execution (R)</th>
            <th scope="col">Waterfall</th>
          </tr>
        </thead>
        <tbody>
          {page.items.map((e) => (
            <tr key={e.id}>
              <td>{e.date}</td>
              <th scope="row">{`${e.symbol} ${e.side}`}</th>
              <td>{e.bucket}</td>
              <td>{referenceWords[e.referenceKind] ?? e.referenceKind}</td>
              <td>{figure(e.totalBps, 2)}</td>
              <td>{figure(e.totalTicks, 1)}</td>
              <td>{figure(e.totalR, 4)}</td>
              <td>{figure(e.delayR, 4)}</td>
              <td>{figure(e.executionR, 4)}</td>
              <td>
                {e.waterfallAvailable ? (
                  <Button
                    size="small"
                    aria-expanded={open === e.id}
                    onClick={() => {
                      setOpen(open === e.id ? null : e.id);
                    }}
                  >
                    {open === e.id ? `Hide waterfall of ${e.symbol}` : `Show waterfall of ${e.symbol}`}
                  </Button>
                ) : (
                  'not closed in the session'
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {open !== null && <WaterfallRows entryId={open} />}
    </section>
  );
}
