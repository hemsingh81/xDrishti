import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type LabRun = Schemas['LabRunView'];
export type LabVerdict = Schemas['LabVerdictView'];
export type LabGate = Schemas['GateView'];
export type LabReport = Schemas['LabReportView'];
export type LabLedger = Schemas['LedgerView'];
export type LabLedgerRow = Schemas['LedgerRowView'];
export type RunMethod = 'quick' | 'full' | 'robust';

export const runKeys = {
  /** Every run list and run record. */
  all: ['lab', 'runs'] as const,
  /** Every run list. */
  lists: ['lab', 'runs', 'list'] as const,
  list: (strategyId: string | null) => ['lab', 'runs', 'list', strategyId ?? 'all'] as const,
  one: (id: string) => ['lab', 'runs', 'one', id] as const,
  report: (id: string) => ['lab', 'runs', 'report', id] as const,
  ledger: (strategyId: string) => ['lab', 'ledger', strategyId] as const,
};

const active = new Set(['queued', 'running']);
export const isActive = (state: string): boolean => active.has(state);

/** How often a run still going is read when the hub does not carry its progress. */
export const pollMs = 3000;
/** How often it is read anyway while the hub is connected, in case an event was missed. */
export const safetyPollMs = 20_000;

/** `poll`: the interval (ms) at which to read again while a run is going, or false for no polling. */
export const runsQuery = (strategyId: string | null, poll: number | false = false) =>
  queryOptions({
    queryKey: runKeys.list(strategyId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/lab/runs', {
          params: { query: { ...(strategyId ? { strategyId } : {}), limit: 50 } },
          signal,
        }),
      ),
    refetchInterval: (q) => (poll && q.state.data?.some((r) => isActive(r.state)) ? poll : false),
  });

export const runQuery = (id: string, poll: number | false = false) =>
  queryOptions({
    queryKey: runKeys.one(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/lab/runs/{id}', { params: { path: { id } }, signal })),
    refetchInterval: (q) => (poll && q.state.data && isActive(q.state.data.state) ? poll : false),
  });

export const reportQuery = (id: string) =>
  queryOptions({
    queryKey: runKeys.report(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/lab/runs/{id}/report', { params: { path: { id } }, signal })),
  });

export const ledgerQuery = (strategyId: string) =>
  queryOptions({
    queryKey: runKeys.ledger(strategyId),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/lab/strategies/{id}/ledger', { params: { path: { id: strategyId } }, signal })),
  });

/** The baskets a run can take as its universe (read here so the feature needs nothing from the baskets feature). */
export const universeBasketsQuery = queryOptions({
  queryKey: ['lab', 'baskets'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/baskets', { signal })),
});

export const reportHtmlHref = (id: string) => `/api/lab/runs/${encodeURIComponent(id)}/report/html`;
export const reportZipHref = (id: string) => `/api/lab/runs/${encodeURIComponent(id)}/report/folder`;

export interface StartRunInput {
  strategyId: string;
  version: number;
  method: RunMethod;
  basketId: string | null;
  symbols: string[];
}

export async function startRun(input: StartRunInput) {
  return unwrap(
    await api.POST('/api/lab/runs', {
      body: {
        strategyId: input.strategyId,
        version: input.version,
        method: input.method,
        universe: input.basketId ? { basketId: input.basketId } : { symbols: input.symbols },
      },
    }),
  );
}

export async function cancelRun(id: string) {
  return unwrap(await api.POST('/api/lab/runs/{id}/cancel', { params: { path: { id } } }));
}
