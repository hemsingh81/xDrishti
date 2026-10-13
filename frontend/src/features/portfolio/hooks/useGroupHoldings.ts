import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { groupHoldingsList } from '../api/groupQueries';
import type { GroupBy, PortfolioFilters } from '../lib/filters';

/**
 * The holdings of one opened group, 50 at a time. Used by the group's expanded body, so the request goes out when the group
 * is first opened; collapsing unmounts it and the pages stay in the query cache for the next time.
 */
export function useGroupHoldings(filters: PortfolioFilters, by: GroupBy, key: string, pollMs: number | false) {
  return useInfiniteRows({ ...groupHoldingsList(filters, by, key), refetchInterval: pollMs });
}
