import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, unwrap, type Schemas } from '@/shared/api';

export type AutoState = Schemas['AutoView'];
export type AutoScope = Schemas['AutoScopeView'];
export type AutoSuggestions = Schemas['AutoSuggestionsView'];
export type AutoSuggestion = Schemas['AutoSuggestionView'];

export const autoKeys = {
  state: ['auto', 'state'] as const,
  scope: ['auto', 'scope'] as const,
  suggestions: ['auto', 'suggestions'] as const,
};

export const autoQuery = queryOptions({
  queryKey: autoKeys.state,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/auto', { signal })),
});

/** The shadow record switch, read from the Paper summary (same key, so the Paper tab and this screen share one cache entry). */
export const autoShadowSwitchQuery = queryOptions({
  queryKey: ['paper', 'shadow-summary'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/shadow/summary', { signal })),
});

export const autoScopeQuery = queryOptions({
  queryKey: autoKeys.scope,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/auto/scope', { signal })),
});

/** The newest suggestions; `null` when there is no plan yet (404) or the newest night failed closed (409): the night readout says why. */
export const autoSuggestionsQuery = queryOptions({
  queryKey: autoKeys.suggestions,
  queryFn: async ({ signal }): Promise<AutoSuggestions | null> => {
    try {
      return unwrap(await api.GET('/api/auto/suggestions', { signal }));
    } catch (error) {
      if (error instanceof ApiError && (error.status === 404 || error.status === 409)) return null;
      throw error;
    }
  },
  retry: false,
});

/** Symbols of one basket's members, to filter the suggestions by basket. */
export const basketSymbolsQuery = (basketId: string) =>
  queryOptions({
    queryKey: ['auto', 'basket-symbols', basketId] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/baskets/{id}/members', { params: { path: { id: basketId } }, signal })).items.map(
        (member) => member.instrument.symbol,
      ),
  });

export const autoBasketsQuery = queryOptions({
  queryKey: ['auto', 'baskets'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/baskets', { signal })),
});

export const autoInstrumentSearchQuery = (text: string) =>
  queryOptions({
    queryKey: ['auto', 'instrument-search', text.trim()] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/instruments', {
          params: { query: { q: text.trim(), pageSize: 20 } },
          signal,
        }),
      ).items,
    enabled: text.trim().length > 0,
    placeholderData: keepPreviousData,
  });

/** Turn Auto ON with the password typed for this act (kept only in the call's variables) or OFF. */
export function useAutoSwitch() {
  const queryClient = useQueryClient();
  const mutation = useMutation({
    gcTime: 0,
    mutationFn: async (input: { on: true; password: string } | { on: false }) =>
      input.on
        ? unwrap(await api.POST('/api/auto/on', { body: { password: input.password } }))
        : unwrap(await api.POST('/api/auto/off')),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['auto'] }),
  });
  return mutation;
}

export function useSetScope() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { basketIds: string[]; instrumentIds: string[] }) =>
      unwrap(await api.PUT('/api/auto/scope', { body })),
    onSuccess: (scope) => {
      queryClient.setQueryData(autoKeys.scope, scope);
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['auto'] }),
  });
}
