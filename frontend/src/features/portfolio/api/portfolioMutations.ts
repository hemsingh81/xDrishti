import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';
import { problemKind } from '../lib/errors';
import { portfolioKeys } from './portfolioQueries';

/** The holdings changed: reload the list, the totals and any open lots. */
export function invalidateHoldings(queryClient: QueryClient) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: portfolioKeys.holdings }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.summary }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.lots }),
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
    onError: (error) => (problemKind(error) === 'conflict' ? invalidateHoldings(queryClient) : undefined),
  });
}
