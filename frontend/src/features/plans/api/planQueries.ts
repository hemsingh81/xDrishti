import { queryOptions } from '@tanstack/react-query';
import { api, ensureOk, unwrap, type Schemas } from '@/shared/api';

export type Plan = Schemas['PlanView'];
export type PlanItem = Schemas['PlanItemView'];
export type PlanReview = Schemas['PlanReviewView'];
export type ReviewTicket = Schemas['ReviewTicketView'];
export type ReviewAccount = Schemas['ReviewAccountView'];
export type ReviewCheck = Schemas['ReviewCheckView'];
export type DecisionOutcome = Schemas['ReviewDecisionOutcome'];
export type ApproveAllOutcome = Schemas['ApproveAllOutcome'];
export type Suggestion = Schemas['SuggestionView'];
export type PlanGrade = Schemas['PlanGradeView'];
export type PlanJournal = Schemas['PlanJournalView'];
export type Digest = Schemas['DigestView'];
export type AutonomySettings = Schemas['AutonomySettingsView'];
export interface Confirmation {
  accountId: string;
  codes: string[];
  reason: string;
}

export const planKeys = {
  all: ['plans'] as const,
  list: ['plans', 'list'] as const,
  one: (id: number) => ['plans', 'one', id] as const,
  review: (id: number) => ['plans', 'review', id] as const,
  suggestions: (id: number) => ['plans', 'suggestions', id] as const,
  grade: (id: number) => ['plans', 'grade', id] as const,
  journal: (id: number) => ['plans', 'journal', id] as const,
  digests: ['plans', 'digests'] as const,
  autonomy: ['plans', 'autonomy'] as const,
  accounts: ['plans', 'accounts'] as const,
};

export const plansQuery = queryOptions({
  queryKey: planKeys.list,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/plans', { params: { query: { limit: 30 } }, signal })),
});

export const planQuery = (id: number) =>
  queryOptions({
    queryKey: planKeys.one(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/plans/{id}', { params: { path: { id } }, signal })),
  });

export const reviewQuery = (id: number) =>
  queryOptions({
    queryKey: planKeys.review(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/plans/{id}/review', { params: { path: { id } }, signal })),
  });

export const suggestionsQuery = (id: number) =>
  queryOptions({
    queryKey: planKeys.suggestions(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/plans/{id}/suggestions', { params: { path: { id } }, signal })),
  });

export const gradeQuery = (id: number) =>
  queryOptions({
    queryKey: planKeys.grade(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/plans/{id}/grade', { params: { path: { id } }, signal })),
  });

export const journalQuery = (id: number) =>
  queryOptions({
    queryKey: planKeys.journal(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/plans/{id}/journal', { params: { path: { id } }, signal })),
  });

export const digestsQuery = queryOptions({
  queryKey: planKeys.digests,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/learning/digests', { params: { query: { limit: 8 } }, signal })),
});

/** Read only: the automatic-approval setting is shown, never changed from this screen. */
export const autonomyQuery = queryOptions({
  queryKey: planKeys.autonomy,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/autonomy/settings', { signal })),
});

export const accountNamesQuery = queryOptions({
  queryKey: planKeys.accounts,
  queryFn: async ({ signal }) => {
    const accounts = unwrap(await api.GET('/api/accounts', { signal }));
    return Object.fromEntries(accounts.map((a) => [a.id, a.name]));
  },
});

export const approveTicket = async (planId: number, itemId: number, confirmations: Confirmation[]) =>
  unwrap(
    await api.POST('/api/plans/{id}/items/{itemId}/approve', {
      params: { path: { id: planId, itemId } },
      body: { reason: null, confirmations },
    }),
  );

export const modifyTicket = async (
  planId: number,
  itemId: number,
  reason: string,
  quantities: { accountId: string; quantity: number }[],
) =>
  unwrap(
    await api.POST('/api/plans/{id}/items/{itemId}/modify', {
      params: { path: { id: planId, itemId } },
      body: { reason, quantities },
    }),
  );

export const rejectTicket = async (planId: number, itemId: number, reason: string) =>
  unwrap(
    await api.POST('/api/plans/{id}/items/{itemId}/reject', {
      params: { path: { id: planId, itemId } },
      body: { reason },
    }),
  );

export const approveAll = async (planId: number) =>
  unwrap(await api.POST('/api/plans/{id}/approve-all', { params: { path: { id: planId } } }));

export const markSeen = async (planId: number, suggestionId: number): Promise<void> => {
  ensureOk(
    await api.POST('/api/plans/{id}/suggestions/{suggestionId}/seen', {
      params: { path: { id: planId, suggestionId } },
    }),
  );
};
