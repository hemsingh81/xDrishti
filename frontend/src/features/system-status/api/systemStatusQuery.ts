import { queryOptions, useQuery } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

/** Query definition is shared by hooks, prefetching and tests — one place for key and fetcher. */
export const systemStatusQuery = queryOptions({
  queryKey: ['system', 'status'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/system/status', { signal })),
  refetchInterval: 10_000,
});

export function useSystemStatus() {
  return useQuery(systemStatusQuery);
}
