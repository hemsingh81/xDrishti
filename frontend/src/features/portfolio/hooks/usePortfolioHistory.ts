import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { istDayIso } from '@/shared/lib/format';
import { useWallClock } from '@/shared/lib/useWallClock';
import { historyQuery } from '../api/historyQueries';
import { defaultHistoryRange, rangeParams, type HistoryRange } from '../lib/historyRange';

/**
 * The value history of the scope bar's account and bucket over the chosen range (chips 1M … All). The range is counted
 * back from today in India; `pollMs` (while the market is open) reads the live point again like the totals.
 */
export function usePortfolioHistory(scope: { account: string | null; bucket: string | null }, pollMs: number | false) {
  const [range, setRange] = useState<HistoryRange>(defaultHistoryRange);
  const today = istDayIso(useWallClock(60_000));
  const query = useQuery({ ...historyQuery({ ...scope, ...rangeParams(range, today) }), refetchInterval: pollMs });
  return { range, setRange, query };
}
