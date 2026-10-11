import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';
import { instrumentsKeys } from './instrumentsQueries';

/** Starts a refresh (202). A 409 means one is already running; callers treat that as "start watching it". */
export function useStartRefresh() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ force }: { force: boolean }) =>
      unwrap(await api.POST('/api/instruments/refresh', { body: { force } })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: instrumentsKeys.refreshStatus }),
  });
}
