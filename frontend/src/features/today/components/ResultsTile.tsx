import { Card, CardContent, Link, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { resultsCalendarQuery } from '@/shared/api/eventsRead';
import { formatNumber, formatWeekdayDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';

/** Sessions the tile looks at: today and the 4 after it (days 0 to 4). */
const windowSessions = 5;

/** Today tile "Results this week": tracked instruments with a visible results meeting in the next 5 sessions, and how many have no entry. */
export default function ResultsTile() {
  const query = useQuery(resultsCalendarQuery);
  const soon = (query.data?.meetings ?? [])
    .filter((m) => m.days >= 0 && m.days < windowSessions)
    .sort((a, b) => a.eventSession.localeCompare(b.eventSession) || a.symbol.localeCompare(b.symbol));
  const without = query.data?.instrumentsWithoutEntry.count ?? 0;
  return (
    <Card component="section" aria-labelledby="results-tile">
      <CardContent>
        <Stack spacing={1}>
          <Typography id="results-tile" variant="h2">
            Results this week
          </Typography>
          {query.isPending && <LoadingState label="Loading results calendar…" />}
          {query.isError && <ErrorState error={query.error} />}
          {query.data && soon.length === 0 && (
            <Typography color="text.secondary">
              No results meetings known for the next {windowSessions} sessions.
            </Typography>
          )}
          {soon.length > 0 && (
            <Stack component="ul" aria-label="Results meetings in the next 5 sessions" sx={{ m: 0, pl: 2.5 }}>
              {soon.map((m) => (
                <li key={m.instrumentId}>
                  <Typography>
                    <strong>{m.symbol}</strong>: {formatWeekdayDate(m.eventSession)} (
                    {m.days === 0 ? 'today' : `in ${String(m.days)} ${m.days === 1 ? 'session' : 'sessions'}`})
                  </Typography>
                </li>
              ))}
            </Stack>
          )}
          {query.data && (
            <Typography variant="body2" color="text.secondary">
              {formatNumber(without)} without a calendar entry.{' '}
              <Link component={RouterLink} to="/data?tab=filings">
                Open the Filings tab
              </Link>
            </Typography>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
}
