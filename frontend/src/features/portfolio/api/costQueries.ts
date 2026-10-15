import { api, unwrap, type CostBatch, type CostBatchPage } from '@/shared/api';
import type { InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';
import { tradeKeys } from './tradeQueries';

/** Imports of depository-charges sheets sit beside the trade imports, so the import history reloads them together. */
export const costKeys = {
  batches: [...tradeKeys.all, 'cost-batches'] as const,
};

/** Cost imports are listed 50 at a time ("Show more" asks for the next page). */
export const costPageSize = 50;

/** The imported depository-charges sheets (all accounts), newest first. */
export const costBatchesList: InfiniteRowsOptions<CostBatch, CostBatchPage> = {
  queryKey: costKeys.batches,
  pageSize: costPageSize,
  getRowId: (batch) => batch.id,
  fetchPage: async (page, { pageSize, signal }) =>
    unwrap(await api.GET('/api/portfolio/cost-batches', { params: { query: { page, pageSize } }, signal })),
};
