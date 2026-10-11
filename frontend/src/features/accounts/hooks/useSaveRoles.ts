import { useMutation } from '@tanstack/react-query';
import { useMakePrimaryData, useUpdateRoles } from '../api/accountMutations';
import type { RolePlan } from '../lib/roleChanges';

/**
 * Applies a role plan to one account: the PUT /roles part first, then make-primary-data. Each request asks for the
 * password when needed (once: the second one finds the session already unlocked). Stops at the first failure.
 */
export function useSaveRoles() {
  const updateRoles = useUpdateRoles();
  const makePrimary = useMakePrimaryData();
  return useMutation({
    mutationFn: async ({ id, plan }: { id: string; plan: RolePlan }) => {
      if (plan.body) {
        await updateRoles.mutateAsync({ id, body: plan.body });
      }
      if (plan.makePrimary) {
        await makePrimary.mutateAsync(id);
      }
    },
  });
}
