import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type LedgerEntry, type LedgerPage } from '@/shared/api';
import type { InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';
import type { LedgerFilter } from '../lib/ledger';
import { portfolioKeys } from './portfolioQueries';

/** Query keys of the Dhan trade history and ledger sync (P2-13): ['portfolio', 'broker-history', …]. */
export const brokerHistoryKeys = {
  all: [...portfolioKeys.all, 'broker-history'] as const,
  statuses: [...portfolioKeys.all, 'broker-history', 'status'] as const,
  status: (accountId: string) => [...portfolioKeys.all, 'broker-history', 'status', accountId] as const,
  ledgers: [...portfolioKeys.all, 'broker-history', 'ledger'] as const,
};

/** While a run runs its status is read this often (and only while the page is visible); otherwise only on a visit. */
export const brokerHistoryPollMs = 3_000;

/** Both kinds' watermark, latest run and progress, the next scheduled run and the automatic rebuild's outcome. */
export const brokerHistoryStatusQuery = (accountId: string) =>
  queryOptions({
    queryKey: brokerHistoryKeys.status(accountId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/accounts/{id}/sync/broker-history/status', { params: { path: { id: accountId } }, signal }),
      ),
    refetchInterval: (query) => (query.state.data?.running ? brokerHistoryPollMs : false),
  });

/** The ledger is listed 50 at a time (the API default; "Show more" asks for the next page). */
export const ledgerPageSize = 50;

/** The account's ledger entries, newest first; the totals of all classes ride on every page (`lastPage.totals`). */
export const ledgerList = (accountId: string, filter: LedgerFilter): InfiniteRowsOptions<LedgerEntry, LedgerPage> => {
  const query = {
    from: filter.from || undefined,
    to: filter.to || undefined,
    class: filter.cls === 'all' ? undefined : filter.cls,
  };
  return {
    queryKey: [...brokerHistoryKeys.ledgers, accountId, query],
    pageSize: ledgerPageSize,
    getRowId: (entry) => entry.id,
    fetchPage: async (page, { pageSize, signal }) =>
      unwrap(
        await api.GET('/api/accounts/{id}/ledger', {
          params: { path: { id: accountId }, query: { ...query, page, pageSize } },
          signal,
        }),
      ),
  };
};
