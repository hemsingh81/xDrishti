import { keepPreviousData, useInfiniteQuery, type QueryKey } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';

/** What a paged list endpoint returns (`page` is 1-based). */
export interface PageResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

/** The page size the screens ask for (the API maximum for the list endpoints). */
export const defaultInfinitePageSize = 100;

export interface InfiniteRowsOptions<T, Page extends PageResult<T> = PageResult<T>> {
  /**
   * Everything `fetchPage` depends on (filters, search, sort) and nothing else: a new key is a new list. Use the same root
   * as before (`['instruments', 'search', …]`) so invalidating the root reloads the list.
   */
  queryKey: QueryKey;
  /** Loads one page. `pageSize` is the hook's `pageSize`; `signal` aborts the request when the list is replaced. */
  fetchPage: (page: number, context: { pageSize: number; signal: AbortSignal }) => Promise<Page>;
  /** Rows per page; default 100. */
  pageSize?: number;
  /** Key of a row; rows with the same key are listed once (pages shift while the list changes under the reader). */
  getRowId?: (item: T) => string;
  /**
   * While the list of a new key loads, keep showing the rows of the previous list, marked `isPlaceholderData` (the screen
   * dims them; the grid does not ask for more of them). Default true, as for the other queries of the app.
   */
  keepPreviousRows?: boolean;
  enabled?: boolean;
}

export interface InfiniteRows<T, Page extends PageResult<T> = PageResult<T>> {
  /** The loaded pages, flattened (and de-duplicated when `getRowId` is given). */
  rows: T[];
  /** The newest page as the endpoint sent it, for what it carries besides the rows (counts, a folder); undefined until the first page is there. */
  lastPage: Page | undefined;
  /** Rows in the whole list, as of the newest page. */
  total: number;
  /** A next page exists. False while the rows are a placeholder for another list or are being refreshed. */
  hasMore: boolean;
  /** Fetches the next page: ignored when there is none, or one is on its way. */
  loadMore: () => void;
  /** The first page is not there yet and there is nothing to show. */
  isPending: boolean;
  isFetching: boolean;
  isFetchingNextPage: boolean;
  /** The rows are those of the previous list while the new one loads. */
  isPlaceholderData: boolean;
  /** The list could not be loaded at all (no rows to show). */
  isError: boolean;
  error: Error | null;
  /** Rows are shown, but a further page (or the refresh of the pages) failed; `loadMore` is not called for it again. */
  loadMoreError: Error | null;
  /** Retries what failed: the next page, or the refresh of the loaded pages. */
  retryMore: () => void;
  /** Reloads the whole list. */
  refetch: () => void;
}

/**
 * Rows of a paged endpoint that grow as the reader scrolls: TanStack `useInfiniteQuery` over pages of `pageSize`, the
 * next page is known from `page * pageSize < total`. Feed it to `Grid`'s `infinite` prop:
 * `{ total, hasMore, loadingMore: isFetchingNextPage, onLoadMore: loadMore, error: loadMoreError && '…', onRetryMore: retryMore }`.
 * Refetching (after an invalidation) reloads every loaded page.
 */
export function useInfiniteRows<T, Page extends PageResult<T> = PageResult<T>>({
  queryKey,
  fetchPage,
  pageSize = defaultInfinitePageSize,
  getRowId,
  keepPreviousRows = true,
  enabled = true,
}: InfiniteRowsOptions<T, Page>): InfiniteRows<T, Page> {
  const query = useInfiniteQuery({
    queryKey,
    enabled,
    initialPageParam: 1,
    queryFn: ({ pageParam, signal }) => fetchPage(pageParam, { pageSize, signal }),
    getNextPageParam: (last) =>
      last.items.length > 0 && last.page * last.pageSize < last.total ? last.page + 1 : undefined,
    placeholderData: keepPreviousRows ? keepPreviousData : undefined,
  });

  const { data, fetchNextPage, refetch } = query;
  const rows = useMemo(() => {
    const all = data?.pages.flatMap((page) => page.items) ?? [];
    if (!getRowId) return all;
    const seen = new Map<string, T>();
    for (const item of all) if (!seen.has(getRowId(item))) seen.set(getRowId(item), item);
    return [...seen.values()];
  }, [data, getRowId]);

  const placeholder = query.isPlaceholderData;
  // While the loaded pages are refreshed in the background no further page is offered; it is offered again afterwards,
  // which makes the grid look at its sentinel anew.
  const refreshing = query.isFetching && !query.isFetchingNextPage;
  const hasMore = query.hasNextPage && !placeholder && !refreshing;
  const canLoad = hasMore && !query.isFetchingNextPage;
  const hasRows = data !== undefined;

  const loadMore = useCallback(() => {
    // Without `cancelRefetch` a second call would cancel the request on its way and start it again.
    if (canLoad) void fetchNextPage({ cancelRefetch: false });
  }, [canLoad, fetchNextPage]);
  const nextPageFailed = query.isFetchNextPageError;
  const retryMore = useCallback(() => {
    void (nextPageFailed ? fetchNextPage({ cancelRefetch: false }) : refetch());
  }, [nextPageFailed, fetchNextPage, refetch]);
  const reload = useCallback(() => {
    void refetch();
  }, [refetch]);

  return {
    rows,
    lastPage: data?.pages.at(-1),
    total: data?.pages.at(-1)?.total ?? 0,
    hasMore,
    loadMore,
    isPending: query.isPending,
    isFetching: query.isFetching,
    isFetchingNextPage: query.isFetchingNextPage,
    isPlaceholderData: placeholder,
    isError: query.isError && !hasRows,
    error: query.isError && !hasRows ? query.error : null,
    loadMoreError: query.isError && hasRows ? query.error : null,
    retryMore,
    refetch: reload,
  };
}
