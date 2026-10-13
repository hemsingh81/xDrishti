import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, portfolioNoticesQuery, unwrap } from '@/shared/api';
import { problemKind } from '../lib/errors';
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
