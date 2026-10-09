import { QueryClient } from '@tanstack/react-query';
import { ApiError } from '@/shared/api';

/** Shared query defaults: cache briefly, avoid refetch storms, never retry client errors (4xx). */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 15_000,
        gcTime: 5 * 60_000,
        refetchOnWindowFocus: false,
        retry: (failureCount, error) => !(error instanceof ApiError && error.status < 500) && failureCount < 2,
      },
    },
  });
}
