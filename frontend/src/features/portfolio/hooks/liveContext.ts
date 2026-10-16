import { useQuery } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useMemo } from 'react';
import type { HoldingRow } from '@/shared/api';
import { liveBaseQuery, type LiveBaseScope } from '../api/liveQueries';
import { applyTick, applyTicks } from '../lib/liveFigures';
import { noTicks, type LiveTickState } from '../lib/liveTicks';
import type { Rereadable } from './useRereadOnMismatch';
import { liveReadMs } from './useLiveFeed';

/** What the parts of the Portfolio screen need to show live prices: the pushed prices, and whether the hub follows the scope. */
export interface LiveValue extends LiveTickState {
  following: boolean;
  scope: LiveBaseScope;
}

export const LiveContext = createContext<LiveValue>({
  ...noTicks,
  following: false,
  scope: { account: null, bucket: null, accounts: [] },
});

export const useLive = (): LiveValue => useContext(LiveContext);

/** Rows at the pushed prices; the same array while no price applies (the grid's rows are memoised by identity). */
export function useLiveRows(rows: readonly HoldingRow[]): readonly HoldingRow[] {
  const { ticks } = useLive();
  return useMemo(() => applyTicks(rows, ticks), [rows, ticks]);
}

/** One row (the open drawer's) at the pushed price. */
export function useLiveRow(row: HoldingRow | undefined): HoldingRow | undefined {
  const { ticks } = useLive();
  return useMemo(() => {
    if (!row?.instrumentId) return row;
    return applyTick(row, ticks.get(row.instrumentId));
  }, [row, ticks]);
}

/**
 * Every open holding of the scope, read while the hub follows the screen (again every 5 minutes and after a reconnect), and
 * the same at the pushed prices. The totals and the group rows are worked out from them. `rows` is undefined until they
 * are read, and when they could not be (the totals then stay as the last read gave them).
 */
export function useLiveBase(): {
  rows: readonly HoldingRow[] | undefined;
  ready: boolean;
  /** When the holdings were read (ms) and how to read them again (for a mismatch with the totals or the groups). */
  read: Rereadable;
} {
  const { following, scope, ticks } = useLive();
  const query = useQuery({ ...liveBaseQuery(scope), enabled: following, refetchInterval: liveReadMs });
  const read = following ? query.data : undefined;
  const rows = useMemo(() => (read === undefined ? undefined : applyTicks(read, ticks)), [read, ticks]);
  const { dataUpdatedAt, refetch } = query;
  const reread = useCallback(() => {
    void refetch({ cancelRefetch: false });
  }, [refetch]);
  return { rows, ready: read !== undefined, read: { updatedAt: dataUpdatedAt, reread } };
}
