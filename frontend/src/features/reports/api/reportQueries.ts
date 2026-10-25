import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';
import { queryString, type ReportQuery } from '../lib/reportFilter';

export type RunsView = Schemas['RunsView'];
export type RunItem = Schemas['RunListItemView'];
export type GroupView = Schemas['GroupView'];
export type GroupsView = Schemas['GroupsView'];
export type PivotView = Schemas['PivotView'];
export type EquityView = Schemas['EquityView'];
export type DistributionView = Schemas['DistributionView'];
export type MaeMfeView = Schemas['ReportMaeMfeView'];
export type InsightsView = Schemas['InsightsView'];
export type WhatWorksView = Schemas['WhatWorksView'];
export type BenchmarksView = Schemas['ReportBenchmarksView'];
export type EvidenceView = Schemas['ReportEvidenceView'];

/** Stored runs are insert-only, so a report stays valid for minutes (P8-08). */
const staleTime = 5 * 60_000;

/** The lens each Overview panel groups by. */
export const lenses = { month: 'month', basket: 'basket', weekday: 'weekday', pivot: 'strategy,timeframe' } as const;

// All reads. This feature calls no write (see readOnly.test.ts). Keys: ['reports', endpoint, canonical query string].
export const runsQuery = queryOptions({
  queryKey: ['reports', 'runs'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/reports/runs', { signal })),
  staleTime,
});

export const summaryQuery = (query: ReportQuery) =>
  queryOptions({
    queryKey: ['reports', 'summary', queryString(query)] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/reports/summary', { params: { query }, signal })),
    staleTime,
    retry: false,
  });

export const groupsQuery = (query: ReportQuery, lens: string) =>
  queryOptions({
    queryKey: ['reports', 'groups', lens, queryString(query)] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/reports/groups', { params: { query: { ...query, lens } }, signal })),
    staleTime,
    retry: false,
  });

export const pivotQuery = (query: ReportQuery, lens: string, metric = 'expectancy_r') =>
  queryOptions({
    queryKey: ['reports', 'pivot', lens, metric, queryString(query)] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/reports/pivot', { params: { query: { ...query, lens, metric } }, signal })),
    staleTime,
    retry: false,
  });

export const equityQuery = (query: ReportQuery, lens?: string) =>
  queryOptions({
    queryKey: ['reports', 'equity', lens ?? '', queryString(query)] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/reports/equity', { params: { query: lens ? { ...query, lens } : query }, signal })),
    staleTime,
    retry: false,
  });

export const distributionQuery = (query: ReportQuery, lens?: string) =>
  queryOptions({
    queryKey: ['reports', 'distribution', lens ?? '', queryString(query)] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/reports/distribution', { params: { query: lens ? { ...query, lens } : query }, signal }),
      ),
    staleTime,
    retry: false,
  });

/** MAE and MFE as counts in bins (P8-10); no lens is the whole population. */
export const maeMfeQuery = (query: ReportQuery, lens?: string) =>
  queryOptions({
    queryKey: ['reports', 'mae-mfe', lens ?? '', queryString(query)] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/reports/mae-mfe', { params: { query: lens ? { ...query, lens } : query }, signal })),
    staleTime,
    retry: false,
  });

export const insightsQuery = (query: ReportQuery) =>
  queryOptions({
    queryKey: ['reports', 'insights', queryString(query)] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/reports/insights', { params: { query }, signal })),
    staleTime,
    retry: false,
  });

export const whatWorksQuery = (query: ReportQuery, lens?: string) =>
  queryOptions({
    queryKey: ['reports', 'what-works', lens ?? '', queryString(query)] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/reports/what-works', { params: { query: lens ? { ...query, lens } : query }, signal }),
      ),
    staleTime,
    retry: false,
  });

/** Buy and hold per basket and the index over the same window (P8-07). The comparison treats any failure as `unavailable`. */
export const benchmarksQuery = (query: ReportQuery) =>
  queryOptions({
    queryKey: ['reports', 'benchmarks', query.basket ?? '', query.from ?? '', query.to ?? ''] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/reports/benchmarks', {
          params: { query: { basket: query.basket, from: query.from, to: query.to } },
          signal,
        }),
      ),
    staleTime,
    retry: false,
  });

/** The baskets the filter can name (id and name); the report itself is sent the ids. */
export const reportBasketsQuery = queryOptions({
  queryKey: ['reports', 'baskets'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/baskets', { signal })),
  staleTime,
});

/**
 * Why a dimension (regime, account) cannot be a lens: the server's own refusal message (422 reports.dimension_unavailable),
 * read once. The server refuses before reading any trade, so the call is cheap. `null` if the server did not refuse.
 */
export const dimensionReasonQuery = (dimension: 'regime' | 'account') =>
  queryOptions({
    queryKey: ['reports', 'dimension', dimension] as const,
    queryFn: async ({ signal }): Promise<string | null> => {
      const result = await api.GET('/api/reports/groups', { params: { query: { lens: dimension } }, signal });
      return result.response.status === 422 ? (result.error?.title ?? result.error?.detail ?? null) : null;
    },
    staleTime: Infinity,
  });
