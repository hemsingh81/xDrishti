import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';
import { formatSort, type CoverageFilters } from '../lib/filters';
import type { Timeframe } from '../lib/timeframes';

/** Query keys: ['data', resource, …]. */
export const dataKeys = {
  all: ['data'] as const,
  overview: ['data', 'overview'] as const,
  coverage: ['data', 'coverage'] as const,
  bars: ['data', 'bars'] as const,
  jobs: ['data', 'jobs'] as const,
  job: (id: string) => ['data', 'job', id] as const,
};

/** How many runs the "Recent runs" list shows. */
export const recentRunCount = 8;

/** How many bars the drawer shows (newest first). */
export const barsTake = 100;

/** The overview without polling; `useDataWatch` adds the interval, so the key (and cache) is shared. */
export const overviewQuery = queryOptions({
  queryKey: dataKeys.overview,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/data/overview', { signal })),
});

/** The API parameters for a table state; defaults are left out. */
export function coverageParamsOf(f: CoverageFilters) {
  return {
    q: f.q.trim() || undefined,
    status: f.status ?? undefined,
    sort: formatSort(f.sort),
    page: f.page,
    pageSize: f.pageSize,
  };
}

export const coverageQuery = (filters: CoverageFilters) => {
  const query = coverageParamsOf(filters);
  return queryOptions({
    queryKey: [...dataKeys.coverage, query] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/data/instruments', { params: { query }, signal })),
    // While the next page or filter loads, keep showing the old rows instead of a blank table.
    placeholderData: keepPreviousData,
  });
};

export const barsQuery = (id: string, timeframe: Timeframe) =>
  queryOptions({
    queryKey: [...dataKeys.bars, id, timeframe] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/data/instruments/{id}/bars', {
          params: { path: { id }, query: { timeframe, take: barsTake } },
          signal,
        }),
      ),
  });

export const jobsQuery = queryOptions({
  queryKey: [...dataKeys.jobs, recentRunCount] as const,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/data/jobs', { params: { query: { take: recentRunCount } }, signal })),
});

export const jobQuery = (id: string) =>
  queryOptions({
    queryKey: dataKeys.job(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/data/jobs/{id}', { params: { path: { id } }, signal })),
  });
