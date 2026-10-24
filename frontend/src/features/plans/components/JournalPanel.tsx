import { Card, CardContent, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { journalQuery } from '../api/planQueries';

/** The auto-journal: fills the sync stored, matched to approved tickets. Read only; nothing here is an order. */
export function JournalPanel({ planId }: { planId: number }) {
  const query = useQuery(journalQuery(planId));
  return (
    <Card component="section" aria-labelledby="plan-journal">
      <CardContent>
        <Typography id="plan-journal" variant="h2" sx={{ mb: 0.5 }}>
          Journal
        </Typography>
        {query.isPending && <LoadingState label="Loading journal…" />}
        {query.isError && <ErrorState error={query.error} />}
        {query.data && (
          <>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              Fills: {query.data.fillBasis}. Plan prices: {query.data.planBasis}. {query.data.ticketsWithoutFills}{' '}
              approved ticket(s) without a matched fill.
            </Typography>
            {query.data.entries.length === 0 && <Typography>No fills matched to tickets.</Typography>}
            <ul style={{ margin: 0, paddingLeft: 18 }}>
              {query.data.entries.map((e) => (
                <li key={`${e.itemId}-${e.accountId}`}>
                  <Typography variant="body2">
                    {e.symbol} {e.side}: {e.fills.length} fill(s), status {e.status}
                    {e.fill ? `, realised fills ${e.fill.r === null ? 'no R' : `${e.fill.r.toFixed(2)} R`}` : ''},
                    plan-price result {e.planR === null ? 'none' : `${e.planR.toFixed(2)} R`}
                    {e.uncertain ? `. Uncertain: ${e.uncertain}` : ''}
                  </Typography>
                </li>
              ))}
            </ul>
            {query.data.discretionary.length > 0 && (
              <Typography variant="body2" sx={{ mt: 1 }}>
                Discretionary fills (no ticket):{' '}
                {query.data.discretionary.map((f) => `${f.symbol} ${f.side} ${f.qty}`).join('; ')}
              </Typography>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
