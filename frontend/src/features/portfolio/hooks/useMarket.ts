import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { servicesTimelineQuery } from '@/shared/api';
import { istDayIso } from '@/shared/lib/format';
import { useWallClock } from '@/shared/lib/useWallClock';
import { invalidateHoldings } from '../api/portfolioMutations';
import { marketPhase } from '../lib/market';

/** While the market is open the holdings and totals are read again this often (and only while the page is visible). */
export const pricePollMs = 60_000;

/**
 * Where the market is now (`phase`: closed, pre-open 09:00-09:15, or open) by the India wall clock (re-read once a minute,
 * and when a hidden tab is shown again) and the trading day of the services timeline. While it is open, `pollMs` is the
 * interval to read prices at; when it closes, they are read once more (the closing quote runs at 15:30, the minute before)
 * and polling stops. The live hub is wanted from pre-open to the close.
 */
export function useMarket() {
  const now = useWallClock(60_000);
  const timeline = useQuery(servicesTimelineQuery(undefined, istDayIso(now)));
  const phase = marketPhase(now, timeline.data);
  const open = phase === 'open';

  const queryClient = useQueryClient();
  const wasOpen = useRef(false);
  useEffect(() => {
    if (wasOpen.current && !open) void invalidateHoldings(queryClient);
    wasOpen.current = open;
  }, [open, queryClient]);

  return { now, open, phase, pollMs: open ? pricePollMs : (false as const) };
}
