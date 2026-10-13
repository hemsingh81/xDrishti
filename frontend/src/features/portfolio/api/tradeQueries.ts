import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import {
  api,
  unwrap,
  type HoldingRow,
  type Trade,
  type TradeBatch,
  type TradeBatchPage,
  type TradePage,
} from '@/shared/api';
import type { InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';
import { portfolioKeys } from './portfolioQueries';

/** Query keys of the trades: all below `['portfolio', 'trades']`, which a rebuild, an undo or a sync reloads. */
export const tradeKeys = {
  all: portfolioKeys.trades,
  batches: [...portfolioKeys.trades, 'batches'] as const,
  /** Every holding's trade list (for the optimistic void, which does not know the holding). */
  holdings: [...portfolioKeys.trades, 'holding'] as const,
  ofHolding: (holdingId: string) => [...portfolioKeys.trades, 'holding', holdingId] as const,
  /** Outside the trades root on purpose: applying a rebuild reloads the trades, and must not ask for the dry run again. */
  rebuildPreview: (holdingId: string) => [...portfolioKeys.all, 'rebuild-preview', holdingId] as const,
};

/** Imports and trades are listed 50 at a time ("Show more" asks for the next page). */
export const tradePageSize = 50;

/** The imported files, newest first, as pages that grow with "Show more". */
export const tradeBatchesList: InfiniteRowsOptions<TradeBatch, TradeBatchPage> = {
  queryKey: tradeKeys.batches,
  pageSize: tradePageSize,
  getRowId: (batch) => batch.id,
  fetchPage: async (page, { pageSize, signal }) =>
    unwrap(await api.GET('/api/portfolio/trade-batches', { params: { query: { page, pageSize } }, signal })),
};

/** The trades of a holding's security, newest first (voided ones included and flagged). */
export const holdingTradesList = (holdingId: string): InfiniteRowsOptions<Trade, TradePage> => ({
  queryKey: tradeKeys.ofHolding(holdingId),
  pageSize: tradePageSize,
  getRowId: (trade) => trade.id,
  keepPreviousRows: false,
  fetchPage: async (page, { pageSize, signal }) =>
    unwrap(
      await api.GET('/api/holdings/{id}/trades', {
        params: { path: { id: holdingId }, query: { page, pageSize } },
        signal,
      }),
    ),
});

/**
 * What a rebuild of one holding's lots would do (a dry run: nothing changes). It is a read, so it is a query; it is never
 * kept (the answer is only true for the moment the owner looks), and a window focus does not ask again.
 */
export const rebuildPreviewQuery = (holdingId: string) =>
  queryOptions({
    queryKey: tradeKeys.rebuildPreview(holdingId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.POST('/api/holdings/{id}/rebuild-lots', {
          params: { path: { id: holdingId }, query: { dryRun: true } },
          signal,
        }),
      ),
    gcTime: 0,
    staleTime: 0,
    refetchOnWindowFocus: false,
  });

/** The open holdings of one account that match what is typed (the Add trade dialog's security picker). */
export const tradeHoldingsQuery = (accountId: string, text: string) =>
  queryOptions({
    queryKey: [...portfolioKeys.lookups, 'trade-holdings', accountId, text.trim()] as const,
    queryFn: async ({ signal }): Promise<HoldingRow[]> =>
      unwrap(
        await api.GET('/api/holdings', {
          params: {
            query: { accountId, q: text.trim() || undefined, status: 'open', sort: 'symbol', page: 1, pageSize: 20 },
          },
          signal,
        }),
      ).items,
    placeholderData: keepPreviousData,
  });

/**
 * What undoing an import would do (a dry run: nothing changes): the trades voided, the holdings rebuilt, the hand-dated and
 * sync lots replaced and the holdings that cannot be rebuilt. Outside the trades root, like the rebuild's dry run.
 */
export const undoPreviewQuery = (batchId: string) =>
  queryOptions({
    queryKey: [...portfolioKeys.all, 'undo-preview', batchId] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.POST('/api/portfolio/trade-batches/{id}/undo', {
          params: { path: { id: batchId }, query: { dryRun: true } },
          signal,
        }),
      ),
    gcTime: 0,
    staleTime: 0,
    refetchOnWindowFocus: false,
  });
