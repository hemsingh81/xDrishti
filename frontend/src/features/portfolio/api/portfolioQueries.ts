import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type HoldingRow } from '@/shared/api';
import type { InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';
import { formatSort, type PortfolioFilters } from '../lib/filters';

/** Query keys: ['portfolio', resource, …]. After a write, `invalidatePortfolio` marks them stale. */
export const portfolioKeys = {
  all: ['portfolio'] as const,
  holdings: ['portfolio', 'holdings'] as const,
  /** The groups of the Group by view and the holdings of each opened group (P2-08). */
  groups: ['portfolio', 'groups'] as const,
  summary: ['portfolio', 'summary'] as const,
  history: ['portfolio', 'history'] as const,
  benchmark: ['portfolio', 'benchmark'] as const,
  lots: ['portfolio', 'lots'] as const,
  lotsOf: (holdingId: string) => ['portfolio', 'lots', holdingId] as const,
  syncStatuses: ['portfolio', 'sync-status'] as const,
  syncStatus: (accountId: string) => ['portfolio', 'sync-status', accountId] as const,
};

/** The API parameters for a page state (without the page); defaults are left out. */
export function holdingsParams(f: PortfolioFilters) {
  return {
    accountId: f.account ?? undefined,
    bucketId: f.bucket ?? undefined,
    status: f.status === 'open' ? undefined : f.status,
    q: f.q.trim() || undefined,
    sort: formatSort(f.sort),
  };
}

const getRowId = (holding: HoldingRow) => holding.id;

/** The holdings the grid shows: pages of 100 (the API maximum) loaded as the owner scrolls (see `useInfiniteRows`). */
export const holdingsList = (filters: PortfolioFilters): InfiniteRowsOptions<HoldingRow> => {
  const query = holdingsParams(filters);
  return {
    queryKey: [...portfolioKeys.holdings, query],
    getRowId,
    fetchPage: async (page, { pageSize, signal }) =>
      unwrap(await api.GET('/api/holdings', { params: { query: { ...query, page, pageSize } }, signal })),
  };
};

/** The totals follow the account and bucket only (not the search, status or page). */
export const summaryQuery = (scope: { account: string | null; bucket: string | null }) => {
  const query = { accountId: scope.account ?? undefined, bucketId: scope.bucket ?? undefined };
  return queryOptions({
    queryKey: [...portfolioKeys.summary, query] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/holdings/summary', { params: { query }, signal })),
  });
};

export const lotsQuery = (holdingId: string) =>
  queryOptions({
    queryKey: portfolioKeys.lotsOf(holdingId),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/holdings/{id}/lots', { params: { path: { id: holdingId } }, signal })),
  });

const runningPollMs = 3_000;
const scheduledPollMs = 60_000;

/** Last run, next scheduled run and mode. Polls fast while a run is in progress, slowly when the daily refresh is on. */
export const syncStatusQuery = (accountId: string) =>
  queryOptions({
    queryKey: portfolioKeys.syncStatus(accountId),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/accounts/{id}/holdings/sync-status', { params: { path: { id: accountId } }, signal })),
    refetchInterval: (query) => {
      const data = query.state.data;
      if (data?.running) return runningPollMs;
      return data && data.mode !== 'off' ? scheduledPollMs : false;
    },
  });
