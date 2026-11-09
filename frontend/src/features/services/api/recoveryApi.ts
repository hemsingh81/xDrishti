import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

/** Query key: ['services', 'recovery', days]. */
export const recoveryKeys = { all: ['services', 'recovery'] as const };

/** What the machine missed and what became of it. Polling is added by the page, only while a pass runs. */
export const recoveryQuery = (days: number) =>
  queryOptions({
    queryKey: [...recoveryKeys.all, days] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/services/recovery', { params: { query: { days } }, signal })),
  });

/** Starts a whole recovery pass (202). 409 `recovery.already_running` while one runs. */
export function useRetryRecovery() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/services/recovery/retry')),
    onSettled: () => queryClient.invalidateQueries({ queryKey: recoveryKeys.all }),
  });
}
