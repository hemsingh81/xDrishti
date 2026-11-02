import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

/** Query keys: ['data', 'filings', …]. Metadata only: the API sends no file content and no path. */
export const filingKinds = ['results', 'shareholding', 'board_meeting', 'announcement'] as const;
export type FilingKindFilter = (typeof filingKinds)[number] | 'all';

export const filingCoverageQuery = queryOptions({
  queryKey: ['data', 'filings', 'coverage'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/filings/coverage', { signal })),
});

export const filingsQuery = (kind: FilingKindFilter) =>
  queryOptions({
    queryKey: ['data', 'filings', 'list', kind] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/filings', { params: { query: kind === 'all' ? {} : { kind } }, signal })),
  });

export const filingFactsQuery = (id: string) =>
  queryOptions({
    queryKey: ['data', 'filings', 'facts', id] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/filings/{id}/facts', { params: { path: { id } }, signal })),
  });
