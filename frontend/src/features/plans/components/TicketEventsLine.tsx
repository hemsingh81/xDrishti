import { Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { eventFilterQuery, resultsCalendarQuery } from '@/shared/api/eventsRead';
import { istDayIso } from '@/shared/lib/format';
import { isNearSession, ticketEvents } from '../lib/ticketEvents';

/**
 * The "Events" line of a ticket on a plan for the current or next session: the days to the results meeting as the calendar
 * said when it was read, and what the event filter would do today. It never states what the plan decided.
 */
export function TicketEventsLine({ symbol, forDate }: { symbol: string; forDate: string }) {
  const near = isNearSession(forDate, istDayIso());
  const calendar = useQuery({ ...resultsCalendarQuery, enabled: near });
  const filter = useQuery({ ...eventFilterQuery, enabled: near });
  const readOn = calendar.data ? istDayIso(new Date(calendar.data.asOf)) : null;
  const stale = readOn !== null && readOn !== istDayIso();
  const refetch = calendar.refetch;
  // A calendar read on an earlier India day is read again: its "sessions away" would be a day out.
  useEffect(() => {
    if (stale) void refetch();
  }, [stale, refetch]);

  if (!near) return null;
  if (calendar.isPending) return <Typography variant="body2">Events: loading…</Typography>;
  if (calendar.isError) {
    return <Typography variant="body2">Events: unavailable — the results calendar could not be read</Typography>;
  }
  const line = ticketEvents({
    symbol,
    calendar: calendar.data,
    filter: filter.data,
    filterFailed: filter.isError,
  });
  return (
    <Typography variant="body2">
      <strong>Events:</strong> {line.results}
      {line.filter ? `; ${line.filter}` : ''}
    </Typography>
  );
}
