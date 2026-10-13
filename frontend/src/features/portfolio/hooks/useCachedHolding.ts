import { useQueryClient, type InfiniteData } from '@tanstack/react-query';
import { useCallback, useSyncExternalStore } from 'react';
import type { HoldingPage, HoldingRow } from '@/shared/api';
import { portfolioKeys } from '../api/portfolioQueries';

const listKey = [...portfolioKeys.groups, 'holdings'];

/**
 * The row of the open holding when it is listed under an opened group (the flat list is not loaded in the grouped view).
 * Read from the query cache and kept in step with it, so the drawer shows the refreshed figures after a refresh. Lists of
 * groups that are open now (type `active`) come first; a list kept from an earlier sort or filter is the last resort, and
 * of several matches the most recently updated one wins, so old figures never beat fresh ones.
 */
export function useCachedHolding(id: string | null): HoldingRow | undefined {
  const queryClient = useQueryClient();
  const cache = queryClient.getQueryCache();
  const snapshot = useCallback((): HoldingRow | undefined => {
    if (id === null) return undefined;
    const rowIn = (type: 'active' | 'inactive') => {
      const matches = cache
        .findAll({ queryKey: listKey, type })
        .map((query) => ({
          row: (query.state.data as InfiniteData<HoldingPage> | undefined)?.pages
            .flatMap((page) => page.items)
            .find((holding) => holding.id === id),
          updatedAt: query.state.dataUpdatedAt,
        }))
        .filter((match) => match.row !== undefined)
        .sort((a, b) => b.updatedAt - a.updatedAt);
      return matches[0]?.row;
    };
    return rowIn('active') ?? rowIn('inactive');
  }, [cache, id]);
  const subscribe = useCallback((notify: () => void) => cache.subscribe(notify), [cache]);
  return useSyncExternalStore(subscribe, snapshot, snapshot);
}
