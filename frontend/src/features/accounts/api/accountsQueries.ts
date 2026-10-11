import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

/** Query keys: ['accounts', resource, …] — invalidated precisely after each write (see accountMutations). */
export const accountsKeys = {
  all: ['accounts'] as const,
  list: ['accounts', 'list'] as const,
  details: ['accounts', 'detail'] as const,
  detail: (id: string) => ['accounts', 'detail', id] as const,
  activities: ['accounts', 'activity'] as const,
  activity: (id: string) => ['accounts', 'activity', id] as const,
  dhanRedirect: ['accounts', 'dhan-redirect'] as const,
  riskProfiles: ['accounts', 'risk-profiles'] as const,
  buckets: ['accounts', 'buckets'] as const,
};

export const accountsQuery = queryOptions({
  queryKey: accountsKeys.list,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/accounts', { signal })),
});

export const accountQuery = (id: string) =>
  queryOptions({
    queryKey: accountsKeys.detail(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/accounts/{id}', { params: { path: { id } }, signal })),
  });

export const activityTake = 50;

export const accountActivityQuery = (id: string) =>
  queryOptions({
    queryKey: accountsKeys.activity(id),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/accounts/{id}/activity', {
          params: { path: { id }, query: { take: activityTake } },
          signal,
        }),
      ),
  });

export const riskProfilesQuery = queryOptions({
  queryKey: accountsKeys.riskProfiles,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/risk-profiles', { signal })),
});

export const bucketsQuery = queryOptions({
  queryKey: accountsKeys.buckets,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/portfolio-buckets', { signal })),
  staleTime: 5 * 60_000,
});

/** The redirect URL to register with the Dhan app. Static for a deployment, so it is cached for an hour. */
export const dhanRedirectQuery = queryOptions({
  queryKey: accountsKeys.dhanRedirect,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/brokers/dhan', { signal })),
  staleTime: 60 * 60_000,
});
