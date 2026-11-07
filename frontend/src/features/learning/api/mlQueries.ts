import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

// CF-10 10d-2b: the reads behind the Models-screen additions. Changing a switch or applying a rollback is an owner act and
// lives in `owner-acts`; it re-reads everything under the `learning` root.
export type MlInfluence = Schemas['MlInfluenceView'];
export type MlTerm = Schemas['MlTermView'];
export type MlHoldOut = Schemas['MlHoldOutView'];
export type RollbackProposals = Schemas['RollbackProposalsView'];
export type RollbackProposal = Schemas['RollbackProposalView'];
export type MlSettings = Schemas['MlSettingsView'];

export const mlKeys = {
  influence: (id: number) => ['learning', 'influence', id] as const,
  rollback: ['learning', 'rollback'] as const,
};

/** Whether one model may influence a decision, term by term. 404 is an answer (no such model), so no retry. */
export const influenceQuery = (id: number) =>
  queryOptions({
    queryKey: mlKeys.influence(id),
    retry: false,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/learning/models/{id}/influence', { params: { path: { id } }, signal })),
  });

/** The rollback proposals, newest first, with the three ML switches beside them. */
export const rollbackQuery = queryOptions({
  queryKey: mlKeys.rollback,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/learning/rollback/proposals', { signal })),
});
