import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, unwrap, type Schemas } from '@/shared/api';

export type SimDays = Schemas['SimDaysView'];
export type SimDaySnapshot = Schemas['SimDaySnapshotView'];
export type SimTapePage = Schemas['SimDayTapeView'];
export type SimDaysStartBody = Schemas['SimDaysStartBody'];

export const simDaysRoot = ['simulation', 'days'] as const;
export const simDaysPollMs = 2_000;

/** The phases in which the runner still moves the run: the status is read every 2 s then, never when it rests. */
export function simDaysInterval(run: SimDays | null | undefined): number | false {
  if (!run) return false;
  if (run.phase === 'settling') return simDaysPollMs;
  return run.auto && (run.phase === 'idle' || run.phase === 'settled') ? simDaysPollMs : false;
}

/** The run, or null when it is not there (404: the start form is shown). */
export const simDaysQuery = (id: string) =>
  queryOptions({
    queryKey: [...simDaysRoot, id] as const,
    queryFn: async ({ signal }): Promise<SimDays | null> => {
      try {
        return unwrap(await api.GET('/api/sim/days/{id}', { params: { path: { id } }, signal }));
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) return null;
        throw error;
      }
    },
    refetchInterval: (query) => simDaysInterval(query.state.data),
  });

/** One day (stored, or the settled cursor day); null when no day is stored on that date (a holiday). */
export const simDayQuery = (id: string, date: string, stored: boolean) =>
  queryOptions({
    queryKey: [...simDaysRoot, id, 'day', date] as const,
    queryFn: async ({ signal }): Promise<SimDaySnapshot | null> => {
      try {
        return unwrap(await api.GET('/api/sim/days/{id}/days/{date}', { params: { path: { id, date } }, signal }));
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) return null;
        throw error;
      }
    },
    // a stored day is immutable; the settled cursor day is read again after an advance
    staleTime: stored ? Infinity : 0,
  });

/** One page of a settled day's tape, [from, to]: nothing after `to` is in it. */
export const simTapeQuery = (id: string, date: string, from: string, to: string) =>
  queryOptions({
    queryKey: [...simDaysRoot, id, 'tape', date, from, to] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/sim/days/{id}/days/{date}/tape', {
          params: { path: { id, date }, query: { from, to } },
          signal,
        }),
      ),
    staleTime: Infinity,
  });

export function useStartSimDays() {
  return useMutation({
    mutationFn: async (body: SimDaysStartBody) => unwrap(await api.POST('/api/sim/days', { body })),
  });
}

export type SimDaysAction = 'settle' | 'advance' | 'continue' | 'cancel' | 'auto-on' | 'auto-off';

const send = async (id: string, action: SimDaysAction): Promise<SimDays> => {
  const params = { path: { id } };
  switch (action) {
    case 'settle':
      return unwrap(await api.POST('/api/sim/days/{id}/settle', { params }));
    case 'advance':
      return unwrap(await api.POST('/api/sim/days/{id}/advance', { params }));
    case 'continue':
      return unwrap(await api.POST('/api/sim/days/{id}/continue', { params }));
    case 'cancel':
      return unwrap(await api.POST('/api/sim/days/{id}/cancel', { params }));
    case 'auto-on':
    case 'auto-off':
      return unwrap(await api.POST('/api/sim/days/{id}/auto', { params, body: { on: action === 'auto-on' } }));
  }
};

/** A control of the run; the answer is the new status, put straight into the cache. */
export function useSimDaysAction(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (action: SimDaysAction) => send(id, action),
    onSuccess: (run) => {
      client.setQueryData(simDaysQuery(id).queryKey, run);
    },
  });
}

/** The API's own sentence for a refusal (sim.invalid, sim.busy, sim.not_settled, …), else a plain fallback. */
export function simProblemText(error: unknown, fallback: string): string {
  if (error instanceof ApiError) return error.problem?.detail ?? error.problem?.title ?? error.message;
  return fallback;
}

/** Stocks by symbol or name, for the scope picker. */
export const simInstrumentSearchQuery = (text: string) =>
  queryOptions({
    queryKey: ['simulation', 'instrument-search', text.trim()] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/instruments', { params: { query: { q: text.trim(), pageSize: 20 } }, signal })).items,
    enabled: text.trim().length > 0,
    placeholderData: keepPreviousData,
  });
