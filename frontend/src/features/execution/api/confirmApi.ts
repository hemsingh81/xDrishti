import { queryOptions, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, unwrap, type Schemas } from '@/shared/api';

export type ConfirmTicket = Schemas['ConfirmTicketView'];
export type DayUnlock = Schemas['DayUnlockView'];

export const ticketsKey = ['execution', 'tickets'] as const;
export const unlocksKey = ['execution', 'day-unlocks'] as const;

const fastPollMs = 2_000;
const slowPollMs = 30_000;

/** Every ticket of the session. Polls fast while one is open and the tab is visible, slowly otherwise. */
export const ticketsQuery = queryOptions({
  queryKey: ticketsKey,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/execution/confirm-tickets', { params: { query: { state: 'all' } }, signal })),
  refetchInterval: (query) =>
    query.state.data?.some((t) => t.state === 'open') && document.visibilityState === 'visible'
      ? fastPollMs
      : slowPollMs,
  refetchIntervalInBackground: true,
});

export const dayUnlocksQuery = queryOptions({
  queryKey: unlocksKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/execution/day-unlocks', { signal })),
  refetchInterval: slowPollMs,
});

/** The server's words for a refused call: the problem title, never a re-typed message. */
export function problemText(error: unknown): string {
  if (error instanceof ApiError) return error.problem?.detail ?? error.problem?.title ?? error.message;
  return 'The request failed.';
}

export function useConfirmTickets() {
  const queryClient = useQueryClient();
  const query = useQuery(ticketsQuery);
  const refresh = () => queryClient.invalidateQueries({ queryKey: ticketsKey });

  const confirm = useMutation({
    mutationFn: async (v: { ticket: ConfirmTicket; password?: string; reason?: string }) =>
      unwrap(
        await api.POST('/api/execution/confirm-tickets/{id}/confirm', {
          params: { path: { id: v.ticket.id }, header: { 'Idempotency-Key': v.ticket.id } },
          body: { textHash: v.ticket.textHash, password: v.password ?? null, reason: v.reason ?? null },
        }),
      ),
    onSettled: refresh,
  });

  const skip = useMutation({
    mutationFn: async (ticket: ConfirmTicket) =>
      unwrap(
        await api.POST('/api/execution/confirm-tickets/{id}/skip', {
          params: { path: { id: ticket.id } },
          body: { reason: null },
        }),
      ),
    onSettled: refresh,
  });

  return { query, confirm, skip };
}

export function useDayUnlock() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { accountId: string; password: string }) =>
      unwrap(await api.POST('/api/execution/day-unlocks', { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: unlocksKey }),
  });
}
