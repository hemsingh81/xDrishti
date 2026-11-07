import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';
import { readinessKeys } from './readinessQueries';

// The only write of the Readiness feature: the owner's enforce switch (ON or OFF, a fresh password both ways).

export type EnforceChange = Schemas['ReadinessSwitchRequest'];

/**
 * Switches `confidence.readiness.enforce`. The password is checked afresh by the server on every change; the typed
 * password does not stay in the mutation cache once nothing watches the mutation. Whatever the answer, the scorecards and the
 * switch are read again.
 */
export function useChangeEnforce() {
  const queryClient = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: async (body: EnforceChange) => unwrap(await api.PUT('/api/confidence/readiness-settings', { body })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: readinessKeys.all }),
  });
}
