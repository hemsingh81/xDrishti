import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

export const essentialsKey = ['settings', 'essentials'] as const;

/** The five Essential settings with their value, default, who changed them and when (AU-10). */
export const essentialsQuery = queryOptions({
  queryKey: essentialsKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/settings/essentials', { signal })),
});

/** Failure notices on or off. The password is for this act only, and is needed to turn them off. */
export function useChangeNotices() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (v: { on: boolean; password: string | null }) =>
      unwrap(
        await api.PUT('/api/settings/essentials/{key}', {
          params: { path: { key: 'notices.failure' } },
          body: { on: v.on, password: v.password },
        }),
      ),
    onSettled: () => queryClient.invalidateQueries({ queryKey: essentialsKey }),
  });
}

/** Puts an Essential setting back to its default, with a fresh password for this act. */
export function useResetEssential() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (v: { key: string; password: string }) =>
      unwrap(
        await api.POST('/api/settings/essentials/{key}/reset', {
          params: { path: { key: v.key } },
          body: { password: v.password },
        }),
      ),
    onSettled: () => queryClient.invalidateQueries({ queryKey: essentialsKey }),
  });
}
