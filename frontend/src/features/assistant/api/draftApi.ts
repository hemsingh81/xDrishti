import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type Draft = Schemas['DraftView'];
export type StrategyIssue = Schemas['StrategyIssue'];
export type BasketOption = Schemas['BasketView'];

/** A finished draft is final (the row is insert-only); a draft being made is asked again every 3 seconds. */
export const draftQuery = (id: number) =>
  queryOptions({
    queryKey: ['assistant', 'draft', id] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/assistant/strategy-drafts/{id}', { params: { path: { id } }, signal })),
    refetchInterval: (query) => (query.state.data?.status === 'drafting' ? 3_000 : false),
    staleTime: 5_000,
  });

export const draftsQuery = queryOptions({
  queryKey: ['assistant', 'drafts'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/assistant/strategy-drafts', { signal })),
  staleTime: 15_000,
});

export const basketsForDraftQuery = queryOptions({
  queryKey: ['assistant', 'draft-baskets'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/baskets', { signal })),
  staleTime: 60_000,
});

/** Validates a text the owner edited with the same validator the Lab uses. Writes nothing. */
export function useValidateText() {
  return useMutation({
    mutationFn: async (text: string) => unwrap(await api.POST('/api/lab/validate', { body: { text, format: 'yaml' } })),
  });
}

export function useStartDraft() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async (idea: string) =>
      unwrap(await api.POST('/api/assistant/strategy-drafts', { body: { idea, basketId: null } })),
    onSuccess: (draft) => {
      client.setQueryData(draftQuery(draft.id).queryKey, draft);
      void client.invalidateQueries({ queryKey: draftsQuery.queryKey });
    },
  });
}

export interface ConfirmVariables {
  id: number;
  text: string | null;
  basketId: string | null;
  symbols: string[] | null;
  run: boolean;
  password: string;
}

/** The owner act: the password travels in this body only and is not kept (no cache of the variables). */
export function useConfirmDraft() {
  const client = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: async ({ id, ...body }: ConfirmVariables) =>
      unwrap(await api.POST('/api/assistant/strategy-drafts/{id}/confirm', { params: { path: { id } }, body })),
    onSuccess: (draft) => {
      client.setQueryData(draftQuery(draft.id).queryKey, draft);
      void client.invalidateQueries({ queryKey: draftsQuery.queryKey });
    },
  });
}

export function useDiscardDraft() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) =>
      unwrap(await api.POST('/api/assistant/strategy-drafts/{id}/discard', { params: { path: { id } } })),
    onSuccess: (draft) => {
      client.setQueryData(draftQuery(draft.id).queryKey, draft);
      void client.invalidateQueries({ queryKey: draftsQuery.queryKey });
    },
  });
}
