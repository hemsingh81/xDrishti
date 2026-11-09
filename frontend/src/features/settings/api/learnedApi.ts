import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

export const learnedKey = ['settings', 'learned'] as const;

/** The learned setting changes, the Auto state, the weekly cap and the allow-list. */
export const learnedQuery = queryOptions({
  queryKey: learnedKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/settings/learned', { signal })),
});

export type LearnedAct = 'approve' | 'undo' | 'dismiss';

/** One explicit act on one change. A password is sent only when the server asked for one for this act. */
export function useLearnedAct() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (v: { id: number; act: LearnedAct; password?: string }) => {
      const params = { path: { id: v.id } };
      const body = v.password === undefined ? undefined : { password: v.password };
      switch (v.act) {
        case 'approve':
          return unwrap(await api.POST('/api/settings/learned/{id}/approve', { params, body }));
        case 'undo':
          return unwrap(await api.POST('/api/settings/learned/{id}/undo', { params, body }));
        case 'dismiss':
          return unwrap(await api.POST('/api/settings/learned/{id}/dismiss', { params }));
      }
    },
    // A refusal (stale, wrong state) means the list moved on: read it again either way.
    onSettled: () => queryClient.invalidateQueries({ queryKey: learnedKey }),
  });
}
