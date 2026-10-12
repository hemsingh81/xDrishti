import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, ensureOk, queryRoots, unwrap, type CalendarDay, type CalendarDayInput } from '@/shared/api';
import { dataKeys } from './dataQueries';

/**
 * What a calendar change moves: the overview's "behind" and proposals count, the list itself, the coverage statuses,
 * the gaps (a gap is a missing session of the calendar), the bell's notices and the Data quality checks (completeness
 * counts the sessions the calendar says exist).
 */
function reloadCalendar(queryClient: QueryClient) {
  return Promise.all(
    [dataKeys.overview, dataKeys.calendar, dataKeys.coverage, dataKeys.gaps, dataKeys.notices, queryRoots.quality].map(
      (queryKey) => queryClient.invalidateQueries({ queryKey }),
    ),
  );
}

export interface AddedDay {
  day: CalendarDay;
  /** 200: the date had an entry (a proposal or an earlier day) and this replaced it. 201: a new date. */
  replaced: boolean;
}

/** Adds a holiday or special session, confirmed, replacing what the date had before. */
export function useAddCalendarDay() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: CalendarDayInput): Promise<AddedDay> => {
      const result = await api.POST('/api/data/calendar', { body });
      const status = result.response.status;
      return { day: unwrap(result), replaced: status === 200 };
    },
    onSettled: () => reloadCalendar(queryClient),
  });
}

/** Confirms a proposal: the day becomes a holiday or special session. */
export function useConfirmCalendarDay() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (date: string) =>
      unwrap(await api.PUT('/api/data/calendar/{date}/confirm', { params: { path: { date } } })),
    onSettled: () => reloadCalendar(queryClient),
  });
}

/** Removes the owner's own day; for a proposal or a day from NSE's list it dismisses it instead (204 either way). */
export function useRemoveCalendarDay() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (date: string) => {
      ensureOk(await api.DELETE('/api/data/calendar/{date}', { params: { path: { date } } }));
    },
    onSettled: () => reloadCalendar(queryClient),
  });
}

/** Reads NSE's public holiday list. Always 200: a refusal comes back as `succeeded: false` with a message. */
export function useRefreshCalendar() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/data/calendar/refresh')),
    onSettled: () => reloadCalendar(queryClient),
  });
}
