import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, portfolioNoticesQuery, unwrap } from '@/shared/api';
import { problemKind } from '../lib/errors';
import { isLiveRead } from './liveQueries';
import { portfolioKeys } from './portfolioQueries';

/**
 * The Action center and the price alerts changed (a decision, an alert created or cancelled): reload the items, the
 * alert lists and the bell's Portfolio notices.
 */
export function invalidateActionCenter(queryClient: QueryClient) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: portfolioKeys.actions }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.alerts }),
    queryClient.invalidateQueries({ queryKey: portfolioNoticesQuery.queryKey }),
  ]);
}

/**
 * The holdings changed: reload the list, the groups (and their opened holdings), the totals, the value history and any
 * open lots. The Action center follows (its items are worked out from the same holdings and prices), and so do the alerts
 * (a quote run may have triggered one).
 */
export function invalidateHoldings(queryClient: QueryClient) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: portfolioKeys.holdings }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.groups }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.summary }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.history }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.lots }),
    // The trades and the import history (a rebuild, an undo or a sync can change what they show).
    queryClient.invalidateQueries({ queryKey: portfolioKeys.trades }),
    // The holdings looked up for the drawer and the alert dialog (their prices and figures changed too).
    queryClient.invalidateQueries({ queryKey: portfolioKeys.lookups }),
    invalidateActionCenter(queryClient),
  ]);
}

/**
 * The income of holdings changed (a dividend was added, voided or accepted): only what carries an `income` is read again - the
 * list, the groups, the totals and the holdings looked up for the drawer. The live reads are included on purpose (the live
 * totals show the income they were read with, and nothing else would refresh them before the 5-minute timer); the value
 * history, the lots, the trades, the Action center and the alerts do not depend on dividends and are left alone.
 */
export function invalidateIncome(queryClient: QueryClient) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: portfolioKeys.holdings }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.groups }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.summary }),
    queryClient.invalidateQueries({ queryKey: [...portfolioKeys.lookups, 'holding'] }),
  ]);
}

/**
 * Only the figures that move with prices are read again: the list, the groups, the totals and the holdings looked up for the
 * drawer. Used when a quote arrives over the live hub and when the hub (re)connects, where the Action center, the history and
 * the trades need no new read.
 *
 * The live reads (`live-base`, `live-members`: every open holding of the scope, paged, and the members of each group) are
 * left out on purpose: they are heavy for the API (a full XIRR per page) and a quote or a reconnect does not change who holds
 * what. They are read on their own 5-minute timer, when they are older than that after a reconnect (`invalidateStaleLive`),
 * and, like everything else, by `invalidateHoldings` after a real change (sync, trade, import).
 */
export function invalidateFigures(queryClient: QueryClient) {
  const notLive = { predicate: (query: { queryKey: readonly unknown[] }) => !isLiveRead(query) };
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: portfolioKeys.holdings, ...notLive }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.groups, ...notLive }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.summary }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.lookups }),
  ]);
}

/** Only the live reads (the holdings behind the live totals, the members of each group) are read again. */
export function invalidateLiveReads(queryClient: QueryClient) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: portfolioKeys.holdings, predicate: isLiveRead }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.groups, predicate: isLiveRead }),
  ]);
}

/**
 * After the hub was away: the live reads that are older than `maxAgeMs` (the 5-minute timer would have read them again by
 * now) are read again; younger ones are kept.
 */
export function invalidateStaleLive(queryClient: QueryClient, maxAgeMs: number) {
  const limit = Date.now() - maxAgeMs;
  const stale = (query: { queryKey: readonly unknown[]; state: { dataUpdatedAt: number } }) =>
    isLiveRead(query) && query.state.dataUpdatedAt < limit;
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: portfolioKeys.holdings, predicate: stale }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.groups, predicate: stale }),
  ]);
}

/** After a sync or a date edit: the holdings and the sync status. */
export function invalidatePortfolio(queryClient: QueryClient) {
  return Promise.all([
    invalidateHoldings(queryClient),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.syncStatuses }),
  ]);
}

/**
 * Sync now. To apply an answer that would close all or most open holdings the owner confirms the numbers they saw;
 * they are sent back (`expectedOpenHoldings`, `expectedWouldClose`) so a changed answer is refused, not applied.
 */
export function useSyncNow(accountId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ confirmed }: { confirmed?: { openHoldings: number; wouldClose: number } } = {}) =>
      unwrap(
        await api.POST('/api/accounts/{id}/holdings/sync', {
          params: { path: { id: accountId } },
          body: confirmed
            ? { confirm: true, expectedOpenHoldings: confirmed.openHoldings, expectedWouldClose: confirmed.wouldClose }
            : undefined,
        }),
      ),
    // Also after a refusal: "already running" and "not connected" change what the status shows.
    onSettled: () => invalidatePortfolio(queryClient),
  });
}

export function useUpdateLotDate(holdingId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ lotId, acquiredOn }: { lotId: string; acquiredOn: string }) =>
      unwrap(
        await api.PUT('/api/holdings/{id}/lots/{lotId}', {
          params: { path: { id: holdingId, lotId } },
          body: { acquiredOn },
        }),
      ),
    onSuccess: () => invalidatePortfolio(queryClient),
    // "The lot was changed or closed meanwhile": load what it is now.
    onError: (error) =>
      ['conflict', 'lot_from_trade'].includes(problemKind(error)) ? invalidateHoldings(queryClient) : undefined,
  });
}
