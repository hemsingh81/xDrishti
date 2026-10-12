import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import { api, queryRoots, unwrap } from '@/shared/api';

/** Query keys: ['baskets', resource, …]. `basketsKeys.all` reloads everything the Baskets screen shows. */
export const basketsKeys = {
  all: ['baskets'] as const,
  list: ['baskets', 'list'] as const,
  /** Every members list (current and as-of) of every basket. */
  members: queryRoots.basketMembers,
  membersOf: (basketId: string, asOf: string | null) => [...queryRoots.basketMembers, basketId, asOf ?? 'now'] as const,
  history: ['baskets', 'history'] as const,
  historyOf: (basketId: string) => ['baskets', 'history', basketId] as const,
  /** The baskets of each instrument (the drawer on the Instruments screen). */
  ofInstruments: ['baskets', 'of-instrument'] as const,
  ofInstrument: (instrumentId: string) => ['baskets', 'of-instrument', instrumentId] as const,
  candidates: ['baskets', 'candidates'] as const,
};

/** The active baskets in the owner's order. */
export const basketsQuery = queryOptions({
  queryKey: basketsKeys.list,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/baskets', { signal })),
});

/** The members of a basket now (`asOf` null) or as they were at the end of an India day (`yyyy-MM-dd`). */
export const membersQuery = (basketId: string, asOf: string | null) =>
  queryOptions({
    queryKey: basketsKeys.membersOf(basketId, asOf),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/baskets/{id}/members', {
          params: { path: { id: basketId }, query: asOf ? { asOf } : {} },
          signal,
        }),
      ),
  });

/** The history shows this many of the newest changes (the API's own limit). */
export const historyLimit = 200;

export const historyQuery = (basketId: string) =>
  queryOptions({
    queryKey: basketsKeys.historyOf(basketId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/baskets/{id}/history', {
          params: { path: { id: basketId }, query: { take: historyLimit } },
          signal,
        }),
      ),
  });

export const instrumentBasketsQuery = (instrumentId: string) =>
  queryOptions({
    queryKey: basketsKeys.ofInstrument(instrumentId),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/instruments/{id}/baskets', { params: { path: { id: instrumentId } }, signal })),
  });

export type CandidateScope = 'all' | 'tracked';

/** Instruments to pick from in the "Add instruments" dialog: the master list or only the tracked ones, retired included. */
export const candidatesQuery = (q: string, scope: CandidateScope) => {
  const query = {
    q: q.trim() || undefined,
    tracked: scope === 'tracked' ? true : undefined,
    includeRetired: true,
    sort: 'symbol',
    page: 1,
    pageSize: 50,
  };
  return queryOptions({
    queryKey: [...basketsKeys.candidates, query] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/instruments', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
};
