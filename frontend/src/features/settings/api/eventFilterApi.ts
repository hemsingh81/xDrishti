import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, unwrap } from '@/shared/api';
import { eventFilterQuery } from '@/shared/api/eventsRead';

export interface EventFilterChange {
  enabled: boolean;
  resultsWithinDays: number;
  approval: 'manual' | 'auto';
  reason: string;
  /** Typed for this change; sent once and not kept. */
  password: string;
}

/** The owner's change of the event filter (PUT with a fresh password every time). The answer replaces the cached view. */
export function useChangeEventFilter() {
  const queryClient = useQueryClient();
  return useMutation({
    // The variables hold the password: the mutation is dropped from the cache as soon as nothing observes it.
    gcTime: 0,
    mutationFn: async (body: EventFilterChange) => unwrap(await api.PUT('/api/selection/event-filter', { body })),
    onSuccess: (view) => {
      queryClient.setQueryData(eventFilterQuery.queryKey, view);
    },
    onError: (error) => {
      // A stale refusal (409) means someone else changed the row first: read it again.
      if (error instanceof ApiError && error.status === 409) {
        void queryClient.invalidateQueries({ queryKey: eventFilterQuery.queryKey });
      }
    },
  });
}
