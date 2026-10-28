import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type LiveVsPaperView = Schemas['LiveVsPaperView'];
export type ExecutionGapView = Schemas['ExecutionGapView'];
export type GapGroup = Schemas['LiveGapGroupView'];
export type ReportEvidence = Schemas['LiveReportEvidenceView'];
export type ReportMetric = Schemas['MetricView'];

export const reportKey = ['live', 'report'] as const;

/** The paired live-versus-paper report of a live account (read only; computed on read). */
export const liveVsPaperQuery = (accountId: string, basis: 'gross' | 'net') =>
  queryOptions({
    queryKey: [...reportKey, 'live-vs-paper', accountId, basis],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/live-trading/reports/live-vs-paper', {
          params: { query: { accountId, basis } },
          signal,
        }),
      ),
  });

/** The execution gap of a live account by strategy or month (read only). */
export const executionGapQuery = (accountId: string, groupBy: 'strategy' | 'month') =>
  queryOptions({
    queryKey: [...reportKey, 'execution-gap', accountId, groupBy],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/live-trading/reports/execution-gap', {
          params: { query: { accountId, groupBy } },
          signal,
        }),
      ),
  });
