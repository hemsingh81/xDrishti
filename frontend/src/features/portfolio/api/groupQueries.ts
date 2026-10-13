import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type HoldingRow } from '@/shared/api';
import type { InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';
import { formatSort, type GroupBy, type PortfolioFilters } from '../lib/filters';
import { groupSortOf, stackByOf } from '../lib/groups';
import { holdingsParams, portfolioKeys } from './portfolioQueries';

/** Holdings of an opened group are shown 50 at a time (a "Show more" row, no scroller inside the page). */
export const groupPageSize = 50;

/** The API parameters of the groups: the scope filters, the grouping, the order (always sent) and the stack dimension. */
export function groupsParams(f: PortfolioFilters, by: GroupBy) {
  return {
    accountId: f.account ?? undefined,
    bucketId: f.bucket ?? undefined,
    status: f.status === 'open' ? undefined : f.status,
    q: f.q.trim() || undefined,
    by,
    sort: formatSort(groupSortOf(f.sort)),
    // The charts need the other dimension; the same answer serves the table, so there is one request.
    stackBy: stackByOf(by),
  };
}

/** The groups of the scope. A new order keeps the rows on screen (dimmed) until it arrives; a new grouping does not. */
export const groupsQuery = (filters: PortfolioFilters, by: GroupBy) => {
  const query = groupsParams(filters, by);
  return queryOptions({
    queryKey: [...portfolioKeys.groups, 'list', query] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/holdings/groups', { params: { query }, signal })),
    placeholderData: (previous) => (previous?.by === by ? previous : undefined),
  });
};

/** The holdings of one group, with the scope's filters and sort (the key is URL-encoded by the client). */
export const groupHoldingsList = (
  filters: PortfolioFilters,
  by: GroupBy,
  key: string,
): InfiniteRowsOptions<HoldingRow> => {
  const query = { ...holdingsParams(filters), groupBy: by, groupKey: key };
  return {
    queryKey: [...portfolioKeys.groups, 'holdings', query],
    pageSize: groupPageSize,
    getRowId: (holding) => holding.id,
    fetchPage: async (page, { pageSize, signal }) =>
      unwrap(await api.GET('/api/holdings', { params: { query: { ...query, page, pageSize } }, signal })),
  };
};
