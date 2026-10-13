import { useMutation, useQueryClient, type InfiniteData } from '@tanstack/react-query';
import { api, unwrap, type ManualTradeInput, type TradePage } from '@/shared/api';
import { findTrade, withTrade, withVoidedTrade } from '../lib/tradeCache';
import { invalidateHoldings, invalidatePortfolio } from './portfolioMutations';
import { importTrades, previewTrades, type TradeUpload } from './tradeUpload';
import { tradeKeys } from './tradeQueries';

/** Reads a tradebook: what importing it would do. Nothing is stored. */
export function usePreviewTrades() {
  return useMutation({ mutationFn: (upload: TradeUpload) => previewTrades(upload) });
}

/**
 * Imports a tradebook. The trades and the import history are read again; so are the holdings, the totals and the
 * rest when lots were rebuilt.
 */
export function useImportTrades() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (upload: TradeUpload) => importTrades(upload),
    onSuccess: (result) =>
      result.rebuilt ? invalidatePortfolio(queryClient) : queryClient.invalidateQueries({ queryKey: tradeKeys.all }),
  });
}

/** Undoes an import (voids its trades; rebuilds the holdings whose lots came from trades). Doing it twice changes nothing. */
export function useUndoBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/portfolio/trade-batches/{id}/undo', { params: { path: { id } } })),
    // Also after a refusal: a sync that was running, or an import that is undone already, changes what the list shows.
    onSettled: () => invalidateHoldings(queryClient),
  });
}

/** Adds a trade by hand. It changes no lots: they are rebuilt on request. */
export function useAddTrade() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: ManualTradeInput) => unwrap(await api.POST('/api/portfolio/trades', { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: tradeKeys.all }),
  });
}

const voidKey = ['portfolio', 'trades', 'void'] as const;

/**
 * Voids a trade: it stays in the list, struck out, at once; when the server refuses it comes back as it was — only that trade,
 * so a void that is still on its way keeps its own state. The lists are read again once the last void has settled.
 */
export function useVoidTrade() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: voidKey,
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/portfolio/trades/{id}/void', { params: { path: { id } } })),
    onMutate: async (id) => {
      await queryClient.cancelQueries({ queryKey: tradeKeys.holdings });
      const lists = queryClient.getQueriesData<InfiniteData<TradePage>>({ queryKey: tradeKeys.holdings });
      const original = findTrade(
        lists.map(([, data]) => data),
        id,
      );
      const now = new Date().toISOString();
      for (const [key, data] of lists) {
        if (data) queryClient.setQueryData(key, withVoidedTrade(data, id, now));
      }
      return { original };
    },
    onError: (_error, _id, context) => {
      const original = context?.original;
      if (!original) return;
      queryClient.setQueriesData<InfiniteData<TradePage>>({ queryKey: tradeKeys.holdings }, (data) =>
        data ? withTrade(data, original) : data,
      );
    },
    onSettled: () => {
      if (queryClient.isMutating({ mutationKey: voidKey }) <= 1) {
        void queryClient.invalidateQueries({ queryKey: tradeKeys.all });
      }
    },
  });
}

/** Replaces a holding's lots by lots built from its trades (the owner has seen the dry run). */
export function useApplyRebuild(holdingId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () =>
      unwrap(
        await api.POST('/api/holdings/{id}/rebuild-lots', {
          params: { path: { id: holdingId }, query: { dryRun: false } },
        }),
      ),
    // Also after a refusal (a sync was running): the lots may have changed under the dialog.
    onSettled: () => invalidatePortfolio(queryClient),
  });
}
