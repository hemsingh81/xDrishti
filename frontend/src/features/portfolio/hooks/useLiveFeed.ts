import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useDebouncedValue } from '@/shared/lib/useDebouncedValue';
import { usePageVisible } from '@/shared/lib/usePageVisible';
import { eventsHub, getEventsHub, useEventsHub, type LiveFeedStatus, type LiveScope } from '@/shared/realtime';
import { liveBaseReadAt, liveStatusQuery } from '../api/liveQueries';
import { invalidateFigures, invalidateLiveReads, invalidateStaleLive } from '../api/portfolioMutations';
import { FirstQuotes } from '../lib/firstQuotes';
import { LiveTickStore } from '../lib/liveTicks';

/** While the hub follows the screen, the figures are read again this often (the pushes keep them moving in between). */
export const liveReadMs = 5 * 60_000;

/** The REST status is read this often while the hub is not connected. */
const restStatusMs = 15_000;

/** A change of the shown rows is sent to the server this long after the last one (scrolling loads pages in bursts). */
const subscribeDelayMs = 300;

/** A newer quote makes the page read its figures again after this wait (the quote's other writes are in by then). */
export const quoteReadDelayMs = 2000;

/** A quote's price time is a little before the moment its previous close is in the database. */
const quoteWriteMs = 10_000;

export interface LiveFeedOptions {
  /** The market is in pre-open or open (by the India clock and the trading day). */
  wanted: boolean;
}

export interface LiveFeed {
  /** The hub is connected and follows the scope: prices move by themselves and the figures are read every 5 minutes. */
  following: boolean;
  /** The answer to the last `Subscribe`: `truncated` means some holdings get no pushes. */
  truncated: boolean;
  /** When following began (ms). */
  followingSince: number | null;
  /** The feed's state: from the hub while following, else from the REST status; null when not known. */
  feed: LiveFeedStatus | null;
  /** The pushed prices of this screen. */
  store: LiveTickStore;
  /** The hub is held (the page is visible and the market is on): the scope may be sent. */
  enabled: boolean;
}

const sortedIds = (ids: readonly string[]): string[] =>
  [...new Set(ids)].sort().slice(0, eventsHub.limits.maxHoldingIds);

/**
 * The live hub for the Portfolio screen: opens the connection while the page is visible and the market is on, keeps the
 * pushed prices in a store,
 * and reads the figures once more after every reconnect (and after the hub was away, so the screen is exact again; the heavy
 * reads of every open holding only when they are older than the 5-minute timer). Without
 * the hub it falls back to the REST status for the chip and to the once-a-minute read of the page.
 */
export function useLiveFeed({ wanted }: LiveFeedOptions): LiveFeed {
  const queryClient = useQueryClient();
  const visible = usePageVisible();
  const enabled = wanted && visible;
  const { hub, state } = useEventsHub(enabled);
  const [store] = useState(() => new LiveTickStore());

  // A quote (every 5 minutes) also writes the previous close, which a push does not carry: the list, the groups and the totals
  // are read again then. The holdings behind the live totals are read again only for the first quote of an instrument in an
  // India day (that is when its previous close changes) that is newer than their last read; otherwise they follow their own
  // 5-minute timer.
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | undefined;
    let withLive = false;
    const firstQuotes = new FirstQuotes();
    const off = hub.onPrices((message) => {
      const known = store.getSnapshot().ticks;
      const quoted = message.instruments.some((price) => {
        const before = known.get(price.instrumentId);
        return price.source === 'quote' && before !== undefined && Date.parse(price.priceAt) > before.at;
      });
      store.apply(message, Date.now());
      const read = liveBaseReadAt(queryClient);
      const newClose = firstQuotes.firstOf(message.instruments).some((at) => read !== null && read < at + quoteWriteMs);
      withLive ||= newClose;
      if ((quoted || newClose) && timer === undefined) {
        timer = setTimeout(() => {
          timer = undefined;
          void invalidateFigures(queryClient);
          if (withLive) void invalidateLiveReads(queryClient);
          withLive = false;
        }, quoteReadDelayMs);
      }
    });
    return () => {
      off();
      clearTimeout(timer);
    };
  }, [hub, store, queryClient]);

  // The figures were read before the hub went away (a hidden tab, a lost connection): once it is back they are read again.
  const away = useRef(false);
  const wasEnabled = useRef(false);
  useEffect(() => {
    if (wasEnabled.current && !enabled) away.current = true;
    wasEnabled.current = enabled;
  }, [enabled]);
  useEffect(() => {
    if (state.connection === 'retrying') away.current = true;
    if (state.connection === 'connected' && away.current) {
      away.current = false;
      void invalidateFigures(queryClient);
      // The holdings the totals are made of are read again only if the 5-minute timer would have by now.
      void invalidateStaleLive(queryClient, liveReadMs);
    }
  }, [state.connection, queryClient]);

  const following = enabled && state.connection === 'connected' && state.ack !== null && !state.ack.failed;
  const rest = useQuery({
    ...liveStatusQuery,
    enabled: enabled && !following,
    refetchInterval: restStatusMs,
  });

  return {
    following,
    truncated: following && state.ack?.truncated === true,
    followingSince: following ? state.ackAt : null,
    feed: following ? state.status : (rest.data ?? null),
    store,
    enabled,
  };
}

export interface LiveScopeOptions {
  /** From `useLiveFeed`: the hub is held. */
  enabled: boolean;
  account: string | null;
  bucket: string | null;
  /** The holdings the screen shows (grid rows, the open drawer): their prices are followed even outside the scope. */
  holdingIds: readonly string[];
}

/**
 * Tells the hub what the screen shows: the scope (account and bucket) for the totals and the groups, and the holdings of the
 * grid and the drawer. A change is sent after a short wait (scrolling loads pages in bursts); leaving unsubscribes.
 */
export function useLiveScope({ enabled, account, bucket, holdingIds }: LiveScopeOptions): void {
  const hub = getEventsHub();
  const scope = useDebouncedValue<LiveScope | null>(
    enabled
      ? {
          accountId: account,
          bucketId: bucket,
          holdingIds: holdingIds.length === 0 ? null : sortedIds(holdingIds),
        }
      : null,
    subscribeDelayMs,
  );
  useEffect(() => {
    hub.setScope(scope);
  }, [hub, scope]);
}
