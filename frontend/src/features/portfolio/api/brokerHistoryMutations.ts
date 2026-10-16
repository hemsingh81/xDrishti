import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';
import { brokerHistoryKeys } from './brokerHistoryQueries';
import { dividendKeys } from './dividendQueries';
import { invalidateHoldings } from './portfolioMutations';

/**
 * A run finished: the status, the ledger, the dividend suggestions read from the ledger (a new ledger entry can be one; only
 * lists on screen are read again, so this costs nothing without the Cash panel) and (the trades and the rebuilt lots changed
 * what they show) the holdings.
 */
export function invalidateBrokerHistory(queryClient: QueryClient) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: brokerHistoryKeys.all }),
    queryClient.invalidateQueries({ queryKey: dividendKeys.all }),
    invalidateHoldings(queryClient),
  ]);
}

/**
 * Sync now for the trades and the ledger: the answer is the trades run as it begins, so only the status is read again
 * (and it then polls while the run runs). Also after a refusal: "already running" and "token expired" change what it shows.
 * With `from` (an India date) the read starts there instead of at the watermark: an earlier date reads dates again, which
 * costs nothing twice (P2-14, ADR 0026); Dhan's refusal of a range comes back in the run's error, a date the API refuses as a
 * 400 on the field `from`.
 */
export function useSyncBrokerHistory(accountId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (options?: { from?: string }) =>
      unwrap(
        await api.POST('/api/accounts/{id}/sync/broker-history', {
          params: { path: { id: accountId } },
          body: options?.from ? { from: options.from } : undefined,
        }),
      ),
    onSettled: () => queryClient.invalidateQueries({ queryKey: brokerHistoryKeys.status(accountId) }),
  });
}
