import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useStepUp } from '@/features/auth';
import { api, unwrap } from '@/shared/api';
import { basketsKeys } from './basketsQueries';

/**
 * "Fetch now" (owner act, step-up): asks every enabled source now. It turns no source on. 409 `baskets.fetch_disabled`
 * while the fetch settings are off.
 */
export function useFetchNow() {
  const queryClient = useQueryClient();
  const { withStepUp } = useStepUp();
  return useMutation({
    mutationFn: withStepUp(async () =>
      unwrap(await api.POST('/api/baskets/system/fetch', { body: { basketIds: null, force: null } })),
    ),
    onSettled: () => queryClient.invalidateQueries({ queryKey: basketsKeys.all }),
  });
}

/** "Recompute now" (owner act, step-up): forms a version of every enabled derived basket. 409 `baskets.derived_disabled` while off. */
export function useRecomputeNow() {
  const queryClient = useQueryClient();
  const { withStepUp } = useStepUp();
  return useMutation({
    mutationFn: withStepUp(async () => unwrap(await api.POST('/api/baskets/system/derived/refresh'))),
    onSettled: () => queryClient.invalidateQueries({ queryKey: basketsKeys.all }),
  });
}
