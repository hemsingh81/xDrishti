import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, queryRoots, unwrap, type CorporateActionInput, type CorporateActionUpdate } from '@/shared/api';
import { dataKeys } from './dataQueries';

/**
 * What a corporate action change moves: the list and its pending count, the overview's pending count, the bell's
 * notice, the Data quality overview, and the bars (an adjusted read changes). The coverage follows with the bars.
 */
function reloadActions(queryClient: QueryClient) {
  return Promise.all(
    [
      dataKeys.corporateActions,
      dataKeys.overview,
      dataKeys.notices,
      dataKeys.coverage,
      dataKeys.bars,
      queryRoots.quality,
    ].map((queryKey) => queryClient.invalidateQueries({ queryKey })),
  );
}

/** Adds an action by hand; it is applied at once. */
export function useAddAction() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: CorporateActionInput) => unwrap(await api.POST('/api/data/corporate-actions', { body })),
    onSettled: () => reloadActions(queryClient),
  });
}

/** Changes an action and makes it applied: `{}` confirms a suggestion as it is; also brings back an ignored one. */
export function useUpdateAction() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: CorporateActionUpdate }) =>
      unwrap(await api.PUT('/api/data/corporate-actions/{id}', { params: { path: { id } }, body })),
    onSettled: () => reloadActions(queryClient),
  });
}

/** Declines a suggestion, or takes an applied action back (the same call; repeating it changes nothing). */
export function useIgnoreAction() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/data/corporate-actions/{id}/ignore', { params: { path: { id } } })),
    onSettled: () => reloadActions(queryClient),
  });
}

/** Runs the detection over the stored day bars; it only ever suggests. */
export function useDetectActions() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/data/corporate-actions/detect')),
    onSettled: () => reloadActions(queryClient),
  });
}

/**
 * Reads the text of a CSV (`symbol,exDate,kind,numerator,denominator[,note]`): `preview` only counts, nothing is saved.
 * `acknowledge` (the owner's consent) lets lines inside already adjusted CSV history in; without it they are a 409.
 */
export function useImportActions() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      text,
      preview,
      acknowledge = false,
    }: {
      text: string;
      preview: boolean;
      acknowledge?: boolean;
    }) =>
      unwrap(
        await api.POST('/api/data/corporate-actions/import', {
          params: { query: { preview, ...(acknowledge ? { acknowledgeCsvAdjusted: true } : {}) } },
          body: text,
          headers: { 'Content-Type': 'text/csv' },
          // The text goes as it is, not as a JSON string.
          bodySerializer: (body) => body,
        }),
      ),
    onSettled: (_result, _error, variables) => (variables.preview ? undefined : reloadActions(queryClient)),
  });
}
