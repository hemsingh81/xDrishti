import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type HistoryOverview = Schemas['HistoryOverview'];
export type HistoryInstrument = Schemas['HistoryInstrumentView'];
export type HistoryBackfillStarted = Schemas['HistoryBackfillStarted'];
export type ProbeSummary = Schemas['ProbeSeamSummary'];

/** Query key: ['data', 'history']. Counts and states only: the API sends no prices and no token. */
export const historyKey = ['data', 'history'] as const;

export const historyQuery = queryOptions({
  queryKey: historyKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/data/history', { signal })),
  staleTime: 15_000,
});

/** Starts the 1-minute backfill (a password-confirmed owner act; the server checks the switch first). */
export function useStartBackfill() {
  const queryClient = useQueryClient();
  return useMutation({
    // The typed password does not stay in the mutation cache.
    gcTime: 0,
    mutationFn: async (password: string) =>
      unwrap(await api.POST('/api/data/history/backfill', { body: { password } })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: historyKey }),
  });
}

/** Probes the seam between the CSV and the backfilled bars (reads only; one request per chosen instrument). */
export function useProbeSeam() {
  const queryClient = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: async (password: string) => unwrap(await api.POST('/api/data/history/probe', { body: { password } })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: historyKey }),
  });
}
