import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from './client';

// Reads of the results calendar and the event filter (P15). They live here, not in one feature, because the Today tile, the
// plan tickets, the Data → Filings tab and the Settings switch card all read them; the change (a write) lives with the switch card.

/** The results meetings known now, default range (today in India and the next 59 days). */
export const resultsCalendarQuery = queryOptions({
  queryKey: ['events', 'results-calendar'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/calendar/results', { signal })),
});

/** The event-risk filter switch with its history and the reasons it is not acting. */
export const eventFilterQuery = queryOptions({
  queryKey: ['events', 'event-filter'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/selection/event-filter', { signal })),
});
