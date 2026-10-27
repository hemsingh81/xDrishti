import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type Proposal = Schemas['ProposalView'];
export type ProposalCheck = Schemas['ProposalCheck'];

/** The newest proposals (pending first is the screen's own ordering); read only, works while generation is off. */
export const proposalsQuery = queryOptions({
  queryKey: ['assistant', 'proposals'] as const,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/assistant/proposals', { params: { query: { limit: 100 } }, signal })),
  staleTime: 15_000,
});

export function useRejectProposal() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: number; reason: string | null }) =>
      unwrap(await api.POST('/api/assistant/proposals/{id}/reject', { params: { path: { id } }, body: { reason } })),
    onSettled: () => client.invalidateQueries({ queryKey: proposalsQuery.queryKey }),
  });
}

/** The owner act: the password travels in this body only and is not kept (no cache of the variables). */
export function useAcceptProposal() {
  const client = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: async ({ id, password }: { id: number; password: string }) =>
      unwrap(await api.POST('/api/assistant/proposals/{id}/accept', { params: { path: { id } }, body: { password } })),
    onSettled: () => client.invalidateQueries({ queryKey: proposalsQuery.queryKey }),
  });
}
