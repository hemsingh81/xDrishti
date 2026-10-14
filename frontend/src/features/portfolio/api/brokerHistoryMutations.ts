import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';
import { brokerHistoryKeys } from './brokerHistoryQueries';
import { invalidateHoldings } from './portfolioMutations';

/** A run finished: the status, the ledger and (the trades and the rebuilt lots changed what they show) the holdings. */
export function invalidateBrokerHistory(queryClient: QueryClient) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: brokerHistoryKeys.all }),
    invalidateHoldings(queryClient),
  ]);
}

/**
 * Sync now for the trades and the ledger: the answer is the trades run as it begins, so only the status is read again
 * (and it then polls while the run runs). Also after a refusal: "already running" and "token expired" change what it shows.
 */
export function useSyncBrokerHistory(accountId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () =>
      unwrap(await api.POST('/api/accounts/{id}/sync/broker-history', { params: { path: { id: accountId } } })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: brokerHistoryKeys.status(accountId) }),
  });
}
