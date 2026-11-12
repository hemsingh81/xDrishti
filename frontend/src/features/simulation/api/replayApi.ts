import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type ReplayRunItem = Schemas['ReplayRunListItem'];
export type ReplayRun = Schemas['ReplayRunView'];
export type ReplayStartBody = Schemas['ReplayStartBody'];

const root = ['simulation', 'replay'] as const;
export const replayPollMs = 3_000;

/** True while a run still moves: the screen reads it often, and stops when it is over. */
export const isReplayActive = (state: string | undefined) =>
  state === 'queued' || state === 'running' || state === 'cancelling';

export const replayRunsQuery = queryOptions({
  queryKey: [...root, 'runs'],
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/replay/runs', { params: { query: { take: 20 } }, signal })),
  refetchInterval: (query) => (query.state.data?.some((run) => isReplayActive(run.state)) ? replayPollMs : false),
});

export const replayRunQuery = (id: string) =>
  queryOptions({
    queryKey: [...root, 'run', id],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/replay/runs/{id}', { params: { path: { id } }, signal })),
    refetchInterval: (query) => (isReplayActive(query.state.data?.state) ? replayPollMs : false),
  });

/** The detail reads follow the run: they are read again while it runs (`active`), never after. */
export const replayEquityQuery = (id: string, active: boolean) =>
  queryOptions({
    queryKey: [...root, 'equity', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/replay/runs/{id}/equity', { params: { path: { id } }, signal })),
    refetchInterval: active ? replayPollMs : false,
  });

export const replaySessionsQuery = (id: string, active: boolean) =>
  queryOptions({
    queryKey: [...root, 'sessions', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/replay/runs/{id}/sessions', { params: { path: { id } }, signal })),
    refetchInterval: active ? replayPollMs : false,
  });

export const replaySessionQuery = (id: string, session: string) =>
  queryOptions({
    queryKey: [...root, 'session', id, session],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/replay/runs/{id}/sessions/{session}', { params: { path: { id, session } }, signal })),
  });

export const replayTradesQuery = (id: string, active: boolean) =>
  queryOptions({
    queryKey: [...root, 'trades', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/replay/runs/{id}/trades', { params: { path: { id } }, signal })),
    refetchInterval: active ? replayPollMs : false,
  });

export const replayPairQuery = (pairId: string, active: boolean) =>
  queryOptions({
    queryKey: [...root, 'pair', pairId],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/replay/pairs/{pairId}', { params: { path: { pairId } }, signal })),
    refetchInterval: active ? replayPollMs : false,
  });

/** What the start form offers: the stored risk profiles and the stored charge sheet, if any. */
export const replayProfilesQuery = queryOptions({
  queryKey: [...root, 'profiles'],
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/risk-profiles', { signal })),
});

export const replayChargeSheetQuery = queryOptions({
  queryKey: [...root, 'charge-sheet'],
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/charge-sheet/confirmation', { signal })),
  retry: false,
});

/** Start a replay: 202 with the run ids; a refusal (422, 409) is shown as its message. */
export function useStartReplay() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: ReplayStartBody) => unwrap(await api.POST('/api/replay/runs', { body })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: root }),
  });
}

export function useCancelReplay(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/replay/runs/{id}/cancel', { params: { path: { id } } })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: root }),
  });
}
