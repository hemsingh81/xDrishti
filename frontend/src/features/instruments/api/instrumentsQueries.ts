import { queryOptions } from '@tanstack/react-query';
import { api, queryRoots, unwrap, type InstrumentSummary } from '@/shared/api';
import { defaultInfinitePageSize, type InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';
import { formatSort, type InstrumentFilters } from '../lib/filters';

/**
 * Query keys: ['instruments', resource, …]. Invalidate `instrumentsKeys.search` to reload the search results (the list
 * and the pages "select all N results" reads); `facets` for the counts.
 */
export const instrumentsKeys = {
  all: queryRoots.instruments,
  search: ['instruments', 'search'] as const,
  facets: ['instruments', 'facets'] as const,
  details: ['instruments', 'detail'] as const,
  detail: (id: string) => ['instruments', 'detail', id] as const,
  refreshStatus: ['instruments', 'refresh', 'status'] as const,
};

/** The API parameters for a page state (without the page); defaults are left out. */
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
  };
}

/** The API maximum; the list and "select all N results" read pages of this size. */
export const searchPageSize = defaultInfinitePageSize;

const getRowId = (item: InstrumentSummary) => item.id;

/** The list the grid shows: pages of 100 loaded as the owner scrolls (see `useInfiniteRows`). */
export const searchList = (filters: InstrumentFilters): InfiniteRowsOptions<InstrumentSummary> => {
  const query = searchParamsOf(filters);
  return {
    queryKey: [...instrumentsKeys.search, 'list', query],
    pageSize: searchPageSize,
    getRowId,
    fetchPage: async (page, { pageSize, signal }) =>
      unwrap(await api.GET('/api/instruments', { params: { query: { ...query, page, pageSize } }, signal })),
  };
};

/** One page, read through the query cache ("select all N results" reads every page of a filter). */
export const searchPageQuery = (filters: InstrumentFilters, page: number) => {
  const query = { ...searchParamsOf(filters), page, pageSize: searchPageSize };
  return queryOptions({
    queryKey: [...instrumentsKeys.search, 'page', query] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/instruments', { params: { query }, signal })),
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
