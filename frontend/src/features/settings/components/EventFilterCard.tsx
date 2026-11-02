import { Box, Paper, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { NonValue } from '@/shared/analytics';
import { eventFilterQuery } from '@/shared/api/eventsRead';
import type { components } from '@/shared/api/schema';
import { ErrorState, LoadingState } from '@/shared/ui';
import { EventFilterForm } from './EventFilterForm';
import { EventFilterHistory } from './EventFilterHistory';

type Reason = components['schemas']['EventFilterReasonView'];

const reasonWords: Readonly<Record<string, string>> = {
  switch_off: 'the switch is off',
  history_unreadable: 'the decision history could not be read (treated as off)',
  calendar_unreadable: 'the results calendar could not be read',
  no_calendar_rows: 'the results calendar has no rows',
  no_entry_for_instruments: 'tracked instruments with no results entry at all',
  no_upcoming_entry_for_instruments:
    'tracked instruments whose last meeting has passed and whose next is not yet announced',
};

const reasonLine = (r: Reason) => `${reasonWords[r.code] ?? r.code}${r.count === null ? '' : `: ${String(r.count)}`}`;

const verdictLine = (verdict: string) =>
  verdict === 'unavailable'
    ? null
    : verdict === 'KEEP_NOT_CONFIRMED'
      ? 'KEEP on development data, not yet confirmed on the hold-out: auto approval will not turn the filter on.'
      : `verdict ${verdict}`;

/** The event-risk filter (ADR 0182): the effective switch, why it may not be acting, the history, and the owner's change form. */
export default function EventFilterCard() {
  const query = useQuery(eventFilterQuery);
  if (query.isPending) return <LoadingState label="Loading the event filter" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const v = query.data;
  const verdict = verdictLine(v.autoVerdict);
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <Paper variant="outlined" component="section" aria-labelledby="event-filter-title" sx={{ p: 2 }}>
        <Typography id="event-filter-title" variant="h3" component="h2" sx={{ mb: 1 }}>
          Event filter
        </Typography>
        <Typography>
          <strong>{v.enabled ? 'On' : 'Off'}</strong>, window N={v.resultsWithinDays} sessions, approval {v.approval}
          {v.decidedBy ? `, last decided by ${v.decidedBy === 'auto' ? 'the automatic rule' : 'the owner'}` : ''}.
        </Typography>
        <Typography variant="body2" sx={{ mt: 0.5 }}>
          Calendar coverage:{' '}
          {v.instrumentsTracked === null || v.instrumentsWithEntry === null ? (
            <NonValue state={{ kind: 'unavailable', reason: 'the calendar could not be read' }} />
          ) : (
            `${String(v.instrumentsWithEntry)} of ${String(v.instrumentsTracked)} tracked instruments have a results entry.`
          )}
        </Typography>
        <Typography variant="body2" sx={{ mt: 0.5 }}>
          Latest verdict for automatic turn-on:{' '}
          {verdict ?? <NonValue state={{ kind: 'unavailable', reason: 'no verdict stored yet' }} />}
        </Typography>
        {v.notActingBecause.length > 0 && (
          <Box component="ul" aria-label="Why the filter is not acting" sx={{ mt: 1, mb: 0, pl: 2.5 }}>
            {v.notActingBecause.map((r) => (
              <li key={r.code}>
                <Typography variant="body2">Not acting: {reasonLine(r)}</Typography>
              </li>
            ))}
          </Box>
        )}
      </Paper>
      <Paper variant="outlined" component="section" aria-labelledby="event-filter-history-title" sx={{ p: 2 }}>
        <Typography id="event-filter-history-title" variant="h3" component="h2" sx={{ mb: 1 }}>
          History (latest 50)
        </Typography>
        <EventFilterHistory rows={v.history} />
      </Paper>
      <Paper variant="outlined" sx={{ p: 2 }}>
        <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
          Change the event filter
        </Typography>
        <EventFilterForm view={v} />
      </Paper>
    </Box>
  );
}
