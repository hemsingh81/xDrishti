import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import {
  api,
  dataJobQuery,
  dataOverviewQuery,
  unwrap,
  type CorporateAction,
  type CorporateActionPage,
  type DataCoveragePage,
  type DataCoverageRow,
  type GapPage,
  type GapRow,
} from '@/shared/api';
import type { InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';
import { formatSort, type CoverageFilters } from '../lib/filters';
import type { ActionsView } from '../lib/actions';
import type { GapsView } from '../lib/gaps';
import type { Timeframe } from '../lib/timeframes';

/** Query keys: ['data', resource, …]. */
export const dataKeys = {
  all: ['data'] as const,
  overview: ['data', 'overview'] as const,
  coverage: ['data', 'coverage'] as const,
  bars: ['data', 'bars'] as const,
  jobs: ['data', 'jobs'] as const,
  calendar: ['data', 'calendar'] as const,
  gaps: ['data', 'gaps'] as const,
  csvFiles: ['data', 'csv-files'] as const,
  corporateActions: ['data', 'corporate-actions'] as const,
  /** The bell's notices (the query itself is in shared/api, which the bell may read without this feature). */
  notices: ['data', 'notices'] as const,
  job: (id: string) => ['data', 'job', id] as const,
};

/** How many runs the Runs tab lists. */
export const recentRunCount = 20;

/** How many bars the drawer shows (newest first). */
export const barsTake = 100;

/** The overview without polling; `useDataWatch` adds the interval, so the key (and cache) is shared. */
export const overviewQuery = dataOverviewQuery;

/** The API parameters for a list state (without the page); defaults are left out. */
export function coverageParamsOf(f: CoverageFilters) {
  return {
    q: f.q.trim() || undefined,
    status: f.status ?? undefined,
    hasGaps: f.hasGaps ? true : undefined,
    sort: formatSort(f.sort),
  };
}

const coverageRowId = (row: DataCoverageRow) => row.id;

/** The coverage list the grid shows: pages of 100 loaded as the owner scrolls (see `useInfiniteRows`). */
export const coverageList = (filters: CoverageFilters): InfiniteRowsOptions<DataCoverageRow, DataCoveragePage> => {
  const query = coverageParamsOf(filters);
  return {
    queryKey: [...dataKeys.coverage, 'list', query],
    getRowId: coverageRowId,
    fetchPage: async (page, { pageSize, signal }) =>
      unwrap(await api.GET('/api/data/instruments', { params: { query: { ...query, page, pageSize } }, signal })),
  };
};

/** The latest bars of an instrument; `adjusted` asks for prices adjusted for the applied corporate actions. */
export const barsQuery = (id: string, timeframe: Timeframe, adjusted = false) =>
  queryOptions({
    queryKey: [...dataKeys.bars, id, timeframe, adjusted] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/data/instruments/{id}/bars', {
          params: { path: { id }, query: { timeframe, take: barsTake, ...(adjusted ? { adjusted } : {}) } },
          signal,
        }),
      ),
  });

export const jobsQuery = queryOptions({
  queryKey: [...dataKeys.jobs, recentRunCount] as const,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/data/jobs', { params: { query: { take: recentRunCount } }, signal })),
});

export const jobQuery = dataJobQuery;

/** The trading calendar: upcoming confirmed days (a year on by default), every proposal, the next holiday. */
export const calendarQuery = queryOptions({
  queryKey: dataKeys.calendar,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/data/calendar', { signal })),
});

const gapRowId = (gap: GapRow) => `${gap.instrumentId}:${gap.date}`;

/** The gaps (ADR 0017 §3), newest first, pages loaded as the owner scrolls; `all` leaves the status filter out. */
export const gapsList = (view: GapsView): InfiniteRowsOptions<GapRow, GapPage> => ({
  queryKey: [...dataKeys.gaps, 'list', view],
  getRowId: gapRowId,
  fetchPage: async (page, { pageSize, signal }) =>
    unwrap(
      await api.GET('/api/data/gaps', {
        params: { query: { status: view === 'all' ? undefined : view, page, pageSize } },
        signal,
      }),
    ),
});

const actionRowId = (action: CorporateAction) => action.id;

/** The corporate actions, newest ex-date first, pages loaded as the owner scrolls; `all` leaves the status filter out. */
export const actionsList = (view: ActionsView): InfiniteRowsOptions<CorporateAction, CorporateActionPage> => ({
  queryKey: [...dataKeys.corporateActions, 'list', view],
  getRowId: actionRowId,
  fetchPage: async (page, { pageSize, signal }) =>
    unwrap(
      await api.GET('/api/data/corporate-actions', {
        params: { query: { status: view === 'all' ? undefined : view, page, pageSize } },
        signal,
      }),
    ),
});

/** The type-ahead of the Add action dialog: instruments matching the typed text (symbol or name). */
export const instrumentLookupQuery = (text: string) =>
  queryOptions({
    queryKey: [...dataKeys.coverage, 'lookup', text.trim()] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/data/instruments', {
          params: { query: { q: text.trim() || undefined, sort: 'symbol', page: 1, pageSize: 20 } },
          signal,
        }),
      ),
    placeholderData: keepPreviousData,
  });
