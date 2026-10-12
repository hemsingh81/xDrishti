import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, queryRoots, unwrap, type TrackResult, type UntrackResult } from '@/shared/api';
import { chunk } from '@/shared/lib/chunks';
import { instrumentsKeys } from './instrumentsQueries';

/**
 * Lists, counts and open details all show the tracked state, so every tracking change reloads them. So do the baskets'
 * members (their Tracked chip), which belong to another feature and are reached by their shared key root.
 */
function useReloadTracking() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: instrumentsKeys.search }),
      queryClient.invalidateQueries({ queryKey: instrumentsKeys.facets }),
      queryClient.invalidateQueries({ queryKey: instrumentsKeys.details }),
      queryClient.invalidateQueries({ queryKey: queryRoots.basketMembers }),
    ]);
}

/**
 * Track instruments (idempotent). More than 500 ids go out in several calls, one after the other: each call is all or
 * nothing, so when a later one fails the earlier ones stay applied (the lists reload either way). The result adds up.
 */
export function useTrackInstruments() {
  const reload = useReloadTracking();
  return useMutation({
    mutationFn: async (ids: readonly string[]): Promise<TrackResult> => {
      const total: TrackResult = { requested: 0, added: 0, reactivated: 0, alreadyTracked: 0, totalTracked: 0 };
      for (const instrumentIds of chunk(ids)) {
        const part = unwrap(await api.POST('/api/instruments/tracked', { body: { instrumentIds } }));
        total.requested += part.requested;
        total.added += part.added;
        total.reactivated += part.reactivated;
        total.alreadyTracked += part.alreadyTracked;
        total.totalTracked = part.totalTracked;
      }
      return total;
    },
    onSettled: reload,
  });
}

/** Untrack instruments (idempotent; their history is kept). Chunked like `useTrackInstruments`. */
export function useUntrackInstruments() {
  const reload = useReloadTracking();
  return useMutation({
    mutationFn: async (ids: readonly string[]): Promise<UntrackResult> => {
      const total: UntrackResult = { requested: 0, untracked: 0, notTracked: 0, totalTracked: 0 };
      for (const instrumentIds of chunk(ids)) {
        const part = unwrap(await api.DELETE('/api/instruments/tracked', { body: { instrumentIds } }));
        total.requested += part.requested;
        total.untracked += part.untracked;
        total.notTracked += part.notTracked;
        total.totalTracked = part.totalTracked;
      }
      return total;
    },
    onSettled: reload,
  });
}
