import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type LiveSlippageView = Schemas['LiveSlippageView'];
export type LiveCapacityView = Schemas['LiveCapacityView'];
export type LiveExecutionsPage = Schemas['LiveExecutionsPage'];
export type LiveExecution = Schemas['LiveExecutionView'];
export type LiveWaterfallView = Schemas['LiveWaterfallView'];
export type EvidenceView = Schemas['LiveExecutionEvidenceView'];
export type MetricView = Schemas['MetricView'];

export const executionKey = ['live', 'execution'] as const;

/** The newest stored slippage snapshot: one row per bucket, side, style and reference kind (read only). */
export const slippageQuery = queryOptions({
  queryKey: [...executionKey, 'slippage'],
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/live-trading/slippage', { signal })),
});

/** The newest stored capacity snapshot (read only). */
export const capacityQuery = queryOptions({
  queryKey: [...executionKey, 'capacity'],
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/live-trading/capacity', { signal })),
});

/** The latest live entries measured against the plan (the last 30 days, newest first; one page). */
export const executionsQuery = queryOptions({
  queryKey: [...executionKey, 'executions'],
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/live-trading/executions', { params: { query: { page: 1, size: 50 } }, signal })),
});

/** The waterfall of one round trip; fetched only when its row is opened. */
export const waterfallQuery = (entryId: number) =>
  queryOptions({
    queryKey: [...executionKey, 'waterfall', entryId],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/live-trading/waterfall', { params: { query: { entryId } }, signal })),
  });
