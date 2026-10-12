import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import { api, queryRoots, unwrap } from '@/shared/api';
import { formatSort, type InstrumentFilters } from '../lib/filters';

/** Query keys: ['instruments', resource, …]. Invalidate `instrumentsKeys.lists` to reload search results and counts. */
export const instrumentsKeys = {
  all: queryRoots.instruments,
  search: ['instruments', 'search'] as const,
  facets: ['instruments', 'facets'] as const,
  details: ['instruments', 'detail'] as const,
  detail: (id: string) => ['instruments', 'detail', id] as const,
  refreshStatus: ['instruments', 'refresh', 'status'] as const,
};

/** The API parameters for a page state; defaults are left out. */
export function searchParamsOf(f: InstrumentFilters) {
  const tracked = f.tab === 'tracked' ? true : f.untracked ? false : undefined;
  return {
    q: f.q.trim() || undefined,
    type: f.type ?? undefined,
    sector: f.sector ?? undefined,
    series: f.series ?? undefined,
    fno: f.fno || undefined,
    asmGsm: f.asmGsm || undefined,
    // The tracked list always includes the tracked instruments that retired, so the switch only matters on the master list.
    includeRetired: (f.tab === 'master' && f.includeRetired) || undefined,
    tracked,
    sort: formatSort(f.sort),
    page: f.page,
    pageSize: f.pageSize,
  };
}

export const searchQuery = (filters: InstrumentFilters) => {
  const query = searchParamsOf(filters);
  return queryOptions({
    queryKey: [...instrumentsKeys.search, query] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/instruments', { params: { query }, signal })),
    // While the next page or filter loads, keep showing the old rows instead of a blank table.
    placeholderData: keepPreviousData,
  });
};

export const facetsQuery = queryOptions({
  queryKey: instrumentsKeys.facets,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/instruments/facets', { signal })),
  staleTime: 30_000,
});

export const detailQuery = (id: string) =>
  queryOptions({
    queryKey: instrumentsKeys.detail(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/instruments/{id}', { params: { path: { id } }, signal })),
  });

/** The status without polling; `useRefreshWatch` adds the interval, so the key (and cache) is shared. */
export const refreshStatusQuery = queryOptions({
  queryKey: instrumentsKeys.refreshStatus,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/instruments/refresh/status', { signal })),
});
