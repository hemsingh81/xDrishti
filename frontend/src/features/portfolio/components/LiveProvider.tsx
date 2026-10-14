import { useMemo, useSyncExternalStore, type ReactNode } from 'react';
import type { LiveBaseScope } from '../api/liveQueries';
import { LiveContext } from '../hooks/liveContext';
import type { LiveFeed } from '../hooks/useLiveFeed';

/**
 * Gives the parts of the screen the prices the hub pushed. Only this component follows the store, so a push renders it and
 * the parts that read the context - not the whole page.
 */
export function LiveProvider({ feed, scope, children }: { feed: LiveFeed; scope: LiveBaseScope; children: ReactNode }) {
  const state = useSyncExternalStore(feed.store.subscribe, feed.store.getSnapshot);
  const value = useMemo(() => ({ ...state, following: feed.following, scope }), [state, feed.following, scope]);
  return <LiveContext value={value}>{children}</LiveContext>;
}
