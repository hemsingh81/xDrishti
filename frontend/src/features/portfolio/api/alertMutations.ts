import { useMutation, useQueryClient, type InfiniteData, type QueryKey } from '@tanstack/react-query';
import { api, unwrap, type PriceAlertInput, type PriceAlertPage } from '@/shared/api';
import { isAlertStatus, type AlertStatus } from '../lib/alerts';
import { withoutActiveAlert, withoutActiveAlertPages } from '../lib/alertCache';
import { invalidateActionCenter } from './portfolioMutations';
import { portfolioKeys } from './portfolioQueries';

/** Create an alert (one-shot, checked on every quotes tick). Reloads the lists and counts, and the bell's notices. */
export function useCreateAlert() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: PriceAlertInput) => unwrap(await api.POST('/api/portfolio/alerts', { body })),
    // Not awaited: the dialog closes on the answer, the lists follow.
    onSuccess: () => {
      void invalidateActionCenter(queryClient);
    },
  });
}

/** [kind, status] of a cached alert query: `['portfolio', 'alerts', 'list' | 'count', status]`. */
function describe(key: QueryKey): { kind: unknown; status: AlertStatus | null } {
  const status: unknown = key[3];
  return { kind: key[2], status: typeof status === 'string' && isAlertStatus(status) ? status : null };
}

/**
 * Cancel an active alert. The Active list and the counts show it gone at once; when the server refuses they go back, and
 * either way they are read again afterwards (an alert that triggered meanwhile stays triggered and shows there).
 */
const cancelKey = ['portfolio', 'alerts', 'cancel'] as const;

export function useCancelAlert() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: cancelKey,
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/portfolio/alerts/{id}/cancel', { params: { path: { id } } })),
    onMutate: async (id) => {
      await queryClient.cancelQueries({ queryKey: portfolioKeys.alerts });
      const previous = queryClient.getQueriesData({ queryKey: portfolioKeys.alerts });
      for (const [key, data] of previous) {
        const { kind, status } = describe(key);
        if (data === undefined || status === null) continue;
        if (kind === 'list') {
          queryClient.setQueryData(key, withoutActiveAlertPages(data as InfiniteData<PriceAlertPage>, id, status));
        } else if (kind === 'count') {
          queryClient.setQueryData(key, withoutActiveAlert(data as PriceAlertPage, id, status));
        }
      }
      return { previous };
    },
    onError: (_error, _id, context) => {
      for (const [key, data] of context?.previous ?? []) queryClient.setQueryData(key, data);
    },
    onSettled: () => {
      if (queryClient.isMutating({ mutationKey: cancelKey }) <= 1) void invalidateActionCenter(queryClient);
    },
  });
}
