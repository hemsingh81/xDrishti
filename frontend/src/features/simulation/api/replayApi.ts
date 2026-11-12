import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type ReplayRunItem = Schemas['ReplayRunListItem'];
export type ReplayRun = Schemas['ReplayRunView'];
export type ReplayArmComparison = Schemas['ReplayArmComparisonView'];
export type ReplayLabel = Schemas['ReplayLabelView'];
export type ReplayStartBody = Schemas['ReplayStartBody'];

export const replayRoot = ['simulation', 'replay'] as const;
const root = replayRoot;
export const replayPollMs = 3_000;

/** True while a run still moves: the screen reads it often, and stops when it is over. */
export const isReplayActive = (state: string | undefined) =>
  state === 'queued' || state === 'running' || state === 'cancelling';

/** How often to read again: only while something moves. Exported so the rule can be tested on its own. */
export const runsInterval = (runs: readonly { state: string }[] | undefined) =>
  runs?.some((run) => isReplayActive(run.state)) ? replayPollMs : false;
export const runInterval = (run: { state: string } | undefined) => (isReplayActive(run?.state) ? replayPollMs : false);
interface ArmState {
  state: string;
}
export const pairInterval = (
  pair:
    | {
        answer: string | null;
        frozen: ArmState | null;
        learning: ArmState | null;
        selectionVsBaseline?: { answer: string | null; selection: ArmState; baseline: ArmState } | null;
      }
    | undefined,
) => {
  if (!pair) return false;
  const base = pair.selectionVsBaseline;
  const answer = base ? base.answer : pair.answer;
  const arms = base ? [base.selection, base.baseline] : [pair.frozen, pair.learning];
  return answer === null && arms.some((arm) => isReplayActive(arm?.state)) ? replayPollMs : false;
};

export const replayRunsQuery = queryOptions({
  queryKey: [...root, 'runs'],
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/replay/runs', { params: { query: { take: 20 } }, signal })),
  refetchInterval: (query) => runsInterval(query.state.data),
});

export const replayRunQuery = (id: string) =>
  queryOptions({
    queryKey: [...root, 'run', id],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/replay/runs/{id}', { params: { path: { id } }, signal })),
    refetchInterval: (query) => runInterval(query.state.data),
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

export const replayPairQuery = (pairId: string) =>
  queryOptions({
    queryKey: [...root, 'pair', pairId],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/replay/pairs/{pairId}', { params: { path: { pairId } }, signal })),
    refetchInterval: (query) => pairInterval(query.state.data),
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

export type ReplaySweepBody = Schemas['ReplaySweepStartBody'];
export type ReplaySweep = Schemas['ReplaySweepView'];
export type ReplayTradeChart = Schemas['ReplayTradeChartView'];

/** A sweep is read again while any start is queued, running or cancelling. */
export const sweepInterval = (sweep: { state: string } | undefined) =>
  sweep?.state === 'running' ? replayPollMs : false;

export const replaySweepQuery = (id: string) =>
  queryOptions({
    queryKey: [...root, 'sweep', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/replay/sweeps/{id}', { params: { path: { id } }, signal })),
    refetchInterval: (query) => sweepInterval(query.state.data),
  });

/** Start a sweep: 202 with the sweep id; a refusal (422 replay.invalid, 409 replay.busy) is shown as its message. */
export function useStartSweep() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: ReplaySweepBody) => unwrap(await api.POST('/api/replay/sweeps', { body })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: root }),
  });
}

export function useCancelSweep(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/replay/sweeps/{id}/cancel', { params: { path: { id } } })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: root }),
  });
}

export const replayTradeChartQuery = (id: string, tradeNo: number, timeframe: string) =>
  queryOptions({
    queryKey: [...root, 'trade-chart', id, tradeNo, timeframe],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/replay/runs/{id}/trades/{tradeNo}/chart', {
          params: { path: { id, tradeNo }, query: { timeframe } },
          signal,
        }),
      ),
  });
