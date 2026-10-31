import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

/** Every write of the Execution screens calls an endpoint that already exists (spec P12-10 criterion 15; ExecutionWritesAllowList.test). */
function useInvalidate() {
  const client = useQueryClient();
  return () => client.invalidateQueries({ queryKey: ['execution'] });
}

export function useStartHalt() {
  const done = useInvalidate();
  return useMutation({
    mutationFn: async (body: Schemas['StartHaltRequest']) => unwrap(await api.POST('/api/execution/halts', { body })),
    onSuccess: done,
  });
}

export function useReleaseHalt() {
  const done = useInvalidate();
  return useMutation({
    mutationFn: async (v: { id: number; body: Schemas['ReleaseHaltRequest'] }) =>
      unwrap(await api.POST('/api/execution/halts/{id}/release', { params: { path: { id: v.id } }, body: v.body })),
    onSuccess: done,
  });
}

export function useRequestPromotion() {
  const done = useInvalidate();
  return useMutation({
    mutationFn: async (v: { accountId: string; body: Schemas['PromotionRequestBody'] }) =>
      unwrap(
        await api.POST('/api/execution/modes/{accountId}/promotion-requests', {
          params: { path: { accountId: v.accountId } },
          body: v.body,
        }),
      ),
    onSuccess: done,
  });
}

export function useConfirmPromotion() {
  const done = useInvalidate();
  return useMutation({
    mutationFn: async (v: { accountId: string; id: string; body: Schemas['PromotionRequestBody'] }) =>
      unwrap(
        await api.POST('/api/execution/modes/{accountId}/promotion-requests/{id}/confirm', {
          params: { path: { accountId: v.accountId, id: v.id } },
          body: v.body,
        }),
      ),
    onSuccess: done,
  });
}

export function useDemote() {
  const done = useInvalidate();
  return useMutation({
    mutationFn: async (v: { accountId: string; body: Schemas['DemotionBody'] }) =>
      unwrap(
        await api.POST('/api/execution/modes/{accountId}/demotions', {
          params: { path: { accountId: v.accountId } },
          body: v.body,
        }),
      ),
    onSuccess: done,
  });
}

export function useChangeLimits() {
  const done = useInvalidate();
  return useMutation({
    mutationFn: async (v: { accountId: string; body: Schemas['LimitsChangeBody'] }) =>
      unwrap(
        await api.PUT('/api/execution/limits/{accountId}', {
          params: { path: { accountId: v.accountId } },
          body: v.body,
        }),
      ),
    onSuccess: done,
  });
}
