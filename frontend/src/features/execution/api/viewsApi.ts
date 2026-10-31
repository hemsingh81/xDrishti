import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

export interface BlotterFilter {
  date?: string;
  accountId?: string;
  stateGroup?: string;
  page?: number;
}
export interface ActivityFilter {
  date?: string;
  kind?: string;
  by?: string;
  page?: number;
}

const clean = <T extends object>(filter: T) =>
  Object.fromEntries(Object.entries(filter).filter(([, v]) => v !== undefined && v !== '')) as T;

export const overviewQuery = (date?: string) =>
  queryOptions({
    queryKey: ['execution', 'overview', date ?? 'today'] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/execution/overview', { params: { query: clean({ date }) }, signal })),
  });

export const blotterQuery = (filter: BlotterFilter) =>
  queryOptions({
    queryKey: ['execution', 'blotter', filter] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/execution/blotter', { params: { query: clean(filter) }, signal })),
  });

export const positionsQuery = (accountId?: string) =>
  queryOptions({
    queryKey: ['execution', 'positions', accountId ?? 'all'] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/execution/positions', { params: { query: clean({ accountId }) }, signal })),
  });

export const activityQuery = (filter: ActivityFilter) =>
  queryOptions({
    queryKey: ['execution', 'activity', filter] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/execution/activity', { params: { query: clean(filter) }, signal })),
  });

export const activeHaltsQuery = queryOptions({
  queryKey: ['execution', 'halts', 'active'] as const,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/execution/halts', { params: { query: { active: true } }, signal })),
});

export const modesQuery = queryOptions({
  queryKey: ['execution', 'modes'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/execution/modes', { signal })),
  refetchInterval: 30_000,
});

export const limitsQuery = (accountId: string) =>
  queryOptions({
    queryKey: ['execution', 'limits', accountId] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/execution/limits', { params: { query: { accountId } }, signal })),
  });

export const phaseCheckQuery = queryOptions({
  queryKey: ['execution', 'phase-check'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/execution/phase-check', { signal })),
});
