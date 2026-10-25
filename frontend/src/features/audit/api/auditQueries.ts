import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type AuditPage = Schemas['AuditPageView'];
export type AuditEntry = Schemas['AuditEntryView'];
export type AuditDetail = Schemas['AuditEntryDetailView'];
export type AuditSummary = Schemas['AuditSummaryView'];
export type AuditKinds = Schemas['AuditKindsView'];

export const pageSize = 50;

// All reads: this feature calls no write (see readOnly.test.ts). Keys: ['audit', resource, ...].
export const entriesQuery = (query: Readonly<Record<string, string>>) =>
  infiniteQueryOptions({
    queryKey: ['audit', 'entries', query] as const,
    queryFn: async ({ pageParam, signal }) =>
      unwrap(
        await api.GET('/api/audit/entries', {
          params: { query: { ...query, limit: pageSize, ...(pageParam ? { cursor: pageParam } : {}) } },
          signal,
        }),
      ),
    initialPageParam: '',
    getNextPageParam: (last) => last.nextCursor ?? undefined,
    retry: false,
  });

export const detailQuery = (id: string) =>
  queryOptions({
    queryKey: ['audit', 'entry', id] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/audit/entries/{id}', { params: { path: { id } }, signal })),
    retry: false,
    staleTime: Infinity,
  });

export const summaryQuery = (from: string | undefined, to: string | undefined) =>
  queryOptions({
    queryKey: ['audit', 'summary', from, to] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/audit/summary', { params: { query: { from, to } }, signal })),
    retry: false,
  });

export const kindsQuery = queryOptions({
  queryKey: ['audit', 'kinds'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/audit/kinds', { signal })),
  staleTime: Infinity,
});
