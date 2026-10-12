import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import type { GridInfinite } from './types';

/** How far below the visible rows the next page is requested. */
const rootMargin = '400px 0px';

interface Options {
  infinite: GridInfinite | undefined;
  /** The rows shown are a stale list while the new one loads: do not ask for more of it. */
  stale: boolean;
  rowCount: number;
  resetKey: string | number | undefined;
  /** The scroll container the sentinel is observed in. */
  root: RefObject<HTMLElement | null>;
}

/**
 * Infinite-scroll plumbing: a sentinel element at the end of the rows is observed (rooted at the scroll container with a
 * 400 px margin); when it nears the viewport `onLoadMore` is called. At most one request is made per page: the observer
 * only runs while loading is allowed (more rows exist, nothing is loading, no error) and a new observer is made after
 * every page, which reports the sentinel at once, so a first page that does not fill the viewport keeps loading until
 * it does. Keys that reach the last row use `requestMore`, which has the same guards.
 */
export function useInfiniteLoad({ infinite, stale, rowCount, resetKey, root }: Options) {
  const [sentinel, setSentinel] = useState<HTMLElement | null>(null);
  const pending = useRef(false);

  const hasMore = infinite?.hasMore ?? false;
  const loadingMore = infinite?.loadingMore ?? false;
  const failed = (infinite?.error ?? null) !== null;
  const ready = infinite !== undefined && hasMore && !loadingMore && !failed && !stale;

  const latest = useRef({ ready, onLoadMore: infinite?.onLoadMore });
  useEffect(() => {
    latest.current = { ready, onLoadMore: infinite?.onLoadMore };
  });

  // A request is "pending" from the call until the parent reports progress (loading started, rows arrived, failed).
  useEffect(() => {
    pending.current = false;
  }, [rowCount, loadingMore, failed, hasMore, stale, resetKey]);

  const requestMore = useCallback(() => {
    if (!latest.current.ready || pending.current) return;
    pending.current = true;
    latest.current.onLoadMore?.();
  }, []);

  useEffect(() => {
    const scroller = root.current;
    if (!ready || !sentinel || !scroller || typeof IntersectionObserver === 'undefined') return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) requestMore();
      },
      { root: scroller, rootMargin },
    );
    observer.observe(sentinel);
    return () => {
      observer.disconnect();
    };
    // `rowCount` re-arms the observer after each page (a fresh observer reports the sentinel's state at once).
  }, [ready, sentinel, rowCount, root, requestMore]);

  return { sentinelRef: setSentinel, requestMore };
}
