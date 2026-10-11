import { api, ensureOk, unwrap, type RiskProfileInput } from '@/shared/api';
import { accountsKeys } from './accountsQueries';
import { useSecuredMutation } from './useSecuredMutation';

export function useCreateRiskProfile() {
  return useSecuredMutation(
    async (body: RiskProfileInput) => unwrap(await api.POST('/api/risk-profiles', { body })),
    (queryClient) => queryClient.invalidateQueries({ queryKey: accountsKeys.riskProfiles }),
  );
}

export function useUpdateRiskProfile() {
  return useSecuredMutation(
    async ({ id, body }: { id: string; body: RiskProfileInput }) =>
      unwrap(await api.PUT('/api/risk-profiles/{id}', { params: { path: { id } }, body })),
    // Accounts show the profile's name and effective limits, so their cached views are stale too.
    (queryClient) =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: accountsKeys.riskProfiles }),
        queryClient.invalidateQueries({ queryKey: accountsKeys.list }),
        queryClient.invalidateQueries({ queryKey: accountsKeys.details }),
      ]),
  );
}

export function useDeleteRiskProfile() {
  return useSecuredMutation(
    async (id: string) => {
      ensureOk(await api.DELETE('/api/risk-profiles/{id}', { params: { path: { id } } }));
    },
    (queryClient) => queryClient.invalidateQueries({ queryKey: accountsKeys.riskProfiles }),
  );
}
