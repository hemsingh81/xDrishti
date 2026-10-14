import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, unwrap, type AcceptSuggestionInput, type DividendInput } from '@/shared/api';
import { dividendKeys } from './dividendQueries';
import { invalidateIncome } from './portfolioMutations';

/**
 * A dividend was added, voided or accepted: its lists, the suggestions and, because the income moved, the holdings, groups and
 * totals - and nothing else (the history, lots, trades and Action center do not change with a dividend).
 */
export function invalidateDividends(queryClient: QueryClient) {
  return Promise.all([queryClient.invalidateQueries({ queryKey: dividendKeys.all }), invalidateIncome(queryClient)]);
}

/** Adds a dividend. A possible duplicate (409) is repeated with `confirm: true` once the owner said it is a second payment. */
export function useAddDividend() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: DividendInput) => unwrap(await api.POST('/api/portfolio/dividends', { body })),
    onSuccess: () => invalidateDividends(queryClient),
  });
}

/** Voids a dividend: it stays in the list, flagged, and stops counting. Voiding a voided one changes nothing. */
export function useVoidDividend() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/portfolio/dividends/{id}/void', { params: { path: { id } } })),
    // Also after a refusal: a dividend that is gone changes what the list shows.
    onSettled: () => invalidateDividends(queryClient),
  });
}

/** Accepts a suggested dividend of the ledger: the amount and date are the entry's; the owner may choose the holding and add a note. */
export function useAcceptSuggestion(accountId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ entryId, body }: { entryId: string; body: AcceptSuggestionInput }) =>
      unwrap(
        await api.POST('/api/accounts/{id}/dividends/suggestions/{entryId}/accept', {
          params: { path: { id: accountId, entryId } },
          body,
        }),
      ),
    // Also after a refusal: a decision taken meanwhile (409) takes the entry off the list.
    onSettled: () => invalidateDividends(queryClient),
  });
}

/** Dismisses a suggested dividend: it is not offered again; nothing else changes. */
export function useDismissSuggestion(accountId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (entryId: string) =>
      unwrap(
        await api.POST('/api/accounts/{id}/dividends/suggestions/{entryId}/dismiss', {
          params: { path: { id: accountId, entryId } },
        }),
      ),
    onSettled: () => queryClient.invalidateQueries({ queryKey: dividendKeys.all }),
  });
}
