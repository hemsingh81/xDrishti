import { Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { waterfallQuery } from '../api/executionQueries';
import { figure } from '../lib/executionLabels';

const opWords: Readonly<Record<string, string>> = { start: 'start', minus: 'minus', total: 'equals' };

/** The waterfall of one round trip, as words and numbers: each line is a plan result, a loss against it, or a subtotal. Fetched when opened. */
export function WaterfallRows({ entryId }: { entryId: number }) {
  const q = useQuery(waterfallQuery(entryId));
  if (q.isPending) return <LoadingState label="Loading the waterfall…" />;
  if (q.isError) return <ErrorState error={q.error} />;
  const w = q.data;
  return (
    <section aria-label={`Waterfall of ${w.symbol}`}>
      {w.rows.length === 0 ? (
        <Typography variant="body2">The waterfall is unavailable for this trip.</Typography>
      ) : (
        <table aria-label="Waterfall rows">
          <thead>
            <tr>
              <th scope="col">Line</th>
              <th scope="col"> </th>
              <th scope="col">R</th>
              <th scope="col">Rupees</th>
            </tr>
          </thead>
          <tbody>
            {w.rows.map((r) => (
              <tr key={r.label}>
                <th scope="row">{r.label}</th>
                <td>{opWords[r.op] ?? r.op}</td>
                <td>{figure(r.r, 4)}</td>
                <td>{r.rupees.toFixed(2)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {w.rows.length > 0 && (
        <Typography variant="body2">{`Result is ${w.basis}; the lines ${w.balanced ? 'add up exactly' : 'do not add up'}.`}</Typography>
      )}
      {w.notes.map((n) => (
        <Typography key={n} variant="body2" color="text.secondary">
          {n}
        </Typography>
      ))}
    </section>
  );
}
