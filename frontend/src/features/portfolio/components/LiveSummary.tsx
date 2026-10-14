import type { UseQueryResult } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import type { HoldingsSummary, PortfolioHistory, ServiceView } from '@/shared/api';
import { useLive, useLiveBase } from '../hooks/liveContext';
import type { LiveFeed } from '../hooks/useLiveFeed';
import { useRereadOnMismatch } from '../hooks/useRereadOnMismatch';
import { notLiveReason } from '../lib/liveNotes';
import { newestLiveTick } from '../lib/liveTicks';
import { liveSummary } from '../lib/liveTotals';
import type { MarketPhase } from '../lib/market';
import { PricesLine } from './PricesLine';
import { SummaryNotes } from './SummaryNotes';
import { SummaryTiles } from './SummaryTiles';

interface LiveSummaryProps {
  summary: UseQueryResult<HoldingsSummary>;
  history: { query: UseQueryResult<PortfolioHistory>; rangeLabel: string };
  market: { now: number; open: boolean; phase: MarketPhase };
  quotes: ServiceView | null;
  quotesUnknown: boolean;
  feed: LiveFeed;
  onReviewDates: () => void;
}

/**
 * The tiles, the prices line and the notes under them, at the prices the hub pushed: the totals are worked out again from
 * the holdings of the scope with the pure functions of `liveTotals`, the chip and the "not live" note from the ticks.
 */
export function LiveSummary({
  summary,
  history,
  market,
  quotes,
  quotesUnknown,
  feed,
  onReviewDates,
}: LiveSummaryProps) {
  const { ticks, skewMs } = useLive();
  const { rows, read } = useLiveBase();
  const data = summary.data;
  const live = useMemo(() => (data && rows ? liveSummary(data, rows) : undefined), [data, rows]);
  // The totals and the holdings were read at different moments and do not fit: the older of the two is read again (rate-limited).
  const { dataUpdatedAt, refetch } = summary;
  const rereadSummary = useCallback(() => {
    void refetch({ cancelRefetch: false });
  }, [refetch]);
  useRereadOnMismatch(data !== undefined && rows !== undefined && live === undefined, [
    read,
    { updatedAt: dataUpdatedAt, reread: rereadSummary },
  ]);
  const shown = live ?? data;

  const newestTickAt = useMemo(
    () =>
      newestLiveTick(
        ticks,
        rows ? rows.flatMap((row) => (row.instrumentId === null ? [] : [row.instrumentId])) : ticks.keys(),
      ),
    [ticks, rows],
  );
  // Holdings without a live tick today: unmapped, beyond the cap, or not traded yet. Only said while the feed streams.
  const notLive = useMemo(() => {
    if (!feed.following || feed.feed?.state !== 'connected' || !rows) return undefined;
    const count = rows.filter((row) => notLiveReason(row, market.now) !== null).length;
    return { count, truncated: feed.truncated };
  }, [feed.following, feed.feed?.state, feed.truncated, rows, market.now]);

  return (
    <>
      <SummaryTiles query={summary} live={live} history={{ query: history.query, rangeLabel: history.rangeLabel }} />
      {shown && (
        <PricesLine
          summary={shown}
          now={market.now}
          marketOpen={market.open}
          quotes={quotes}
          quotesUnknown={quotesUnknown}
          live={{
            phase: market.phase,
            following: feed.following,
            feed: feed.feed,
            followingSince: feed.followingSince,
            skewMs,
            newestTickAt,
          }}
        />
      )}
      {shown && (
        <SummaryNotes
          defaultDateLots={shown.defaultDateLots}
          unpricedCount={shown.unpricedCount}
          dayChangeExcluded={shown.dayChangeExcluded}
          onReviewDates={onReviewDates}
          notLive={notLive}
        />
      )}
    </>
  );
}
