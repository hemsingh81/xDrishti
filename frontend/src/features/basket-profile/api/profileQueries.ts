import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type BasketProfile = Schemas['BasketProfileView'];
export type BasketRanking = Schemas['BasketRankingView'];

/** The window and the index of a profile or a ranking; every field is optional (the server has defaults and says which). */
export interface ProfileParams {
  from?: string | null;
  to?: string | null;
  index?: string | null;
  asOf?: string | null;
}

// A stored bar changes the answer only at the end of a session, and the server caches by the newest bar: five minutes here.
const staleTime = 5 * 60_000;

const opt = (value: string | null | undefined) => value ?? undefined;

// All reads. This feature calls no write (see readOnly.test.ts). Keys sit under the Reports root.
export const profileQuery = (basketId: string, p: ProfileParams) =>
  queryOptions({
    queryKey: ['reports', 'basket-profile', basketId, p.from ?? '', p.to ?? '', p.index ?? '', p.asOf ?? ''] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/reports/baskets/{id}/profile', {
          params: {
            path: { id: basketId },
            query: { from: opt(p.from), to: opt(p.to), index: opt(p.index), membersAsOf: opt(p.asOf) },
          },
          signal,
        }),
      ),
    staleTime,
    retry: false,
  });

export const rankingQuery = (baskets: readonly string[], p: ProfileParams) =>
  queryOptions({
    queryKey: ['reports', 'basket-ranking', baskets.join(','), p.from ?? '', p.to ?? '', p.index ?? ''] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/reports/baskets/ranking', {
          params: { query: { basket: baskets.join(','), from: opt(p.from), to: opt(p.to), index: opt(p.index) } },
          signal,
        }),
      ),
    enabled: baskets.length >= 2,
    staleTime,
    retry: false,
  });

/** The strategy results of the basket in the default run set: the row Reports already shows (P8-09), read, not recomputed. */
export const strategyRowQuery = (basketId: string) =>
  queryOptions({
    queryKey: ['reports', 'basket-profile', 'strategy', basketId] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/reports/groups', { params: { query: { lens: 'basket', basket: basketId } }, signal })),
    staleTime,
    retry: false,
  });

/** Why basket by regime by strategy is not shown: the server's own refusal of the regime lens (422), read once. */
export const regimeReasonQuery = queryOptions({
  queryKey: ['reports', 'basket-profile', 'regime-reason'] as const,
  queryFn: async ({ signal }): Promise<string | null> => {
    const result = await api.GET('/api/reports/groups', { params: { query: { lens: 'regime' } }, signal });
    return result.response.status === 422 ? (result.error?.title ?? result.error?.detail ?? null) : null;
  },
  staleTime: Infinity,
});

/** The baskets a ranking can name. */
export const basketListQuery = queryOptions({
  queryKey: ['reports', 'basket-profile', 'baskets'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/baskets', { signal })),
  staleTime,
});
