import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, queryRoots, unwrap } from '@/shared/api';
import { portfolioKeys } from './portfolioQueries';

/**
 * Tracking changed which instruments have price history: the history and the benchmark figures are read again, and so are
 * the screens that show the tracked state (Instruments, Data & import, the baskets' members), by their shared roots.
 */
function invalidateTracking(queryClient: QueryClient) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: portfolioKeys.history }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.summary }),
    queryClient.invalidateQueries({ queryKey: portfolioKeys.benchmark }),
    queryClient.invalidateQueries({ queryKey: queryRoots.instruments }),
    queryClient.invalidateQueries({ queryKey: queryRoots.basketMembers }),
    queryClient.invalidateQueries({ queryKey: queryRoots.data }),
  ]);
}

/** "Track my holdings": track the instruments of the open holdings in the scope (idempotent; starts no job). */
export function useTrackHoldings(scope: { account: string | null; bucket: string | null }) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/portfolio/history/track-holdings', {
          params: { query: { accountId: scope.account ?? undefined, bucketId: scope.bucket ?? undefined } },
        }),
      ),
    onSettled: () => invalidateTracking(queryClient),
  });
}

/** "Track the benchmark": track the benchmark index (idempotent; starts no job). */
export function useTrackBenchmark() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/portfolio/history/track-benchmark')),
    onSettled: () => invalidateTracking(queryClient),
  });
}

/** Choose another master-list index as the benchmark (no screen yet; the API and this hook are ready for one). */
export function useSetBenchmark() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (instrumentId: string) =>
      unwrap(await api.PUT('/api/portfolio/benchmark', { body: { instrumentId } })),
    onSettled: () => invalidateTracking(queryClient),
  });
}
