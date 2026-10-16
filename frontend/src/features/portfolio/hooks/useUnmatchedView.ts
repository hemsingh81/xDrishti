import { useCallback, useEffect, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import type { HoldingRow } from '@/shared/api';
import type { InfiniteRows } from '@/shared/lib/useInfiniteRows';

/** `?unmatched=1`: the holdings that are not in the instrument master list (the bell's "Securities with no match"). */
export const useUnmatchedParam = (): boolean => useSearchParams()[0].get('unmatched') === '1';

/**
 * The flat holdings list narrowed to the holdings with no match in the instrument master list, while `on`. The API has no such
 * filter, so every page is read (a holding without a match can be on any of them) and the rows are filtered here. Off, the list
 * is passed through as it is.
 */
export function useUnmatchedView(on: boolean, result: InfiniteRows<HoldingRow>) {
  const [, setParams] = useSearchParams();
  const { hasMore, loadMore, isFetchingNextPage, loadMoreError, rows } = result;

  useEffect(() => {
    if (on && hasMore && !isFetchingNextPage && !loadMoreError) loadMore();
  }, [on, hasMore, isFetchingNextPage, loadMoreError, loadMore]);

  const unmatched = useMemo(() => (on ? rows.filter((holding) => !holding.mapped) : rows), [on, rows]);
  const shown = useMemo<InfiniteRows<HoldingRow>>(
    () => (on ? { ...result, rows: unmatched, total: unmatched.length, hasMore: false } : result),
    [on, result, unmatched],
  );

  const showAll = useCallback(() => {
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete('unmatched');
        return next;
      },
      { replace: true },
    );
  }, [setParams]);

  return { result: shown, count: unmatched.length, reading: on && hasMore, showAll };
}
