import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import { api, unwrap, type QualityInstrumentRow } from '@/shared/api';
import type { InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';
import { formatSort, type QualityFilters } from '../lib/filters';
import type { AggregateTimeframe } from '../lib/labels';

/** Query keys: ['quality', resource, …]. */
export const qualityKeys = {
  all: ['quality'] as const,
  overview: ['quality', 'overview'] as const,
  instruments: ['quality', 'instruments'] as const,
  detail: ['quality', 'detail'] as const,
  runs: ['quality', 'runs'] as const,
};

/** How many runs the history shows. */
export const runsTake = 20;

/** Counts, last run, gate and window. */
export const overviewQuery = queryOptions({
  queryKey: qualityKeys.overview,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/data/quality/overview', { signal })),
});

/** The API parameters for a table state (without the page); defaults are left out. */
export function instrumentsParamsOf(f: QualityFilters) {
  return {
    q: f.q.trim() || undefined,
    status: f.status ?? undefined,
    sort: formatSort(f.sort),
  };
}

const getRowId = (row: QualityInstrumentRow) => row.instrumentId;

/** The list the grid shows: pages of 100 loaded as the owner scrolls (see `useInfiniteRows`). */
export const instrumentsList = (filters: QualityFilters): InfiniteRowsOptions<QualityInstrumentRow> => {
  const query = instrumentsParamsOf(filters);
  return {
    queryKey: [...qualityKeys.instruments, 'list', query],
    getRowId,
    fetchPage: async (page, { pageSize, signal }) =>
      unwrap(
        await api.GET('/api/data/quality/instruments', { params: { query: { ...query, page, pageSize } }, signal }),
      ),
  };
};

export interface DetailOptions {
  differencesOnly: boolean;
  /** One timeframe of the stored-vs-recomputed table; null = all. */
  timeframe: AggregateTimeframe | null;
}

/** One instrument's checks and the stored-vs-recomputed buckets (computed when asked, so always the state now). */
export const detailQuery = (id: string, { differencesOnly, timeframe }: DetailOptions) =>
  queryOptions({
    queryKey: [...qualityKeys.detail, id, { differencesOnly, timeframe }] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/data/quality/instruments/{id}', {
          params: {
            path: { id },
            query: { differencesOnly: differencesOnly || undefined, timeframe: timeframe ?? undefined },
          },
          signal,
        }),
      ),
    // Toggling "Differences only" keeps the drawer filled while the table is loading.
    placeholderData: keepPreviousData,
  });

export const runsQuery = queryOptions({
  queryKey: [...qualityKeys.runs, runsTake] as const,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/data/quality/runs', { params: { query: { take: runsTake } }, signal })),
});
