import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  api,
  portfolioNoticesQuery,
  queryRoots,
  unwrap,
  type ActionThresholdsInput,
  type ActionThresholdsView,
} from '@/shared/api';

export const thresholdsKey = ['portfolio', 'action-thresholds'] as const;

/** The thresholds in force with the documented defaults. */
export const thresholdsQuery = queryOptions({
  queryKey: thresholdsKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/portfolio/action-thresholds', { signal })),
});

/**
 * A threshold changed the next read of the Action center: the saved view replaces the cached one, and the Portfolio
 * screen's items and the bell's count (reached by their shared keys, not by importing that feature) are read again.
 */
function useAfterChange() {
  const queryClient = useQueryClient();
  return (view: ActionThresholdsView) => {
    queryClient.setQueryData(thresholdsKey, view);
    // Not awaited: the saved view answers the form at once, the other screens' lists follow.
    void queryClient.invalidateQueries({ queryKey: queryRoots.portfolioActions });
    void queryClient.invalidateQueries({ queryKey: portfolioNoticesQuery.queryKey });
  };
}

export function useSaveThresholds() {
  const afterChange = useAfterChange();
  return useMutation({
    mutationFn: async (body: ActionThresholdsInput) =>
      unwrap(await api.PUT('/api/portfolio/action-thresholds', { body })),
    onSuccess: afterChange,
  });
}

export function useResetThresholds() {
  const afterChange = useAfterChange();
  return useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/portfolio/action-thresholds/reset')),
    onSuccess: afterChange,
  });
}
