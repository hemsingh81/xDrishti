import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import {
  api,
  unwrap,
  type DividendPage,
  type DividendSuggestionPage,
  type DividendView,
  type HoldingRow,
  type LedgerEntry,
} from '@/shared/api';
import type { InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';
import { portfolioKeys } from './portfolioQueries';

/** Query keys of the dividends (P2-14): all below `['portfolio', 'dividends']`, which an add, a void or a decision reloads. */
export const dividendKeys = {
  all: [...portfolioKeys.all, 'dividends'] as const,
  ofHolding: (holdingId: string) => [...portfolioKeys.all, 'dividends', 'holding', holdingId] as const,
  ofAccount: (accountId: string) => [...portfolioKeys.all, 'dividends', 'account', accountId] as const,
  suggestions: (accountId: string) => [...portfolioKeys.all, 'dividends', 'suggestions', accountId] as const,
};

/** Dividends are listed 50 at a time (the API default; "Show more" asks for the next page). */
export const dividendPageSize = 50;

/** The dividends entered for a holding, newest first (voided ones included and flagged). */
export const holdingDividendsList = (holdingId: string): InfiniteRowsOptions<DividendView, DividendPage> => ({
  queryKey: dividendKeys.ofHolding(holdingId),
  pageSize: dividendPageSize,
  getRowId: (dividend) => dividend.id,
  keepPreviousRows: false,
  fetchPage: async (page, { pageSize, signal }) =>
    unwrap(
      await api.GET('/api/holdings/{id}/dividends', {
        params: { path: { id: holdingId }, query: { page, pageSize } },
        signal,
      }),
    ),
});

/** The dividends entered for an account (every holding's and the unallocated ones), newest first. */
export const accountDividendsList = (accountId: string): InfiniteRowsOptions<DividendView, DividendPage> => ({
  queryKey: dividendKeys.ofAccount(accountId),
  pageSize: dividendPageSize,
  getRowId: (dividend) => dividend.id,
  keepPreviousRows: false,
  fetchPage: async (page, { pageSize, signal }) =>
    unwrap(
      await api.GET('/api/accounts/{id}/dividends', {
        params: { path: { id: accountId }, query: { page, pageSize } },
        signal,
      }),
    ),
});

/** Ledger entries of class dividend the owner has not accepted or dismissed (usually none: Dhan's ledger has no ordinary dividend). */
export const suggestionsList = (accountId: string): InfiniteRowsOptions<LedgerEntry, DividendSuggestionPage> => ({
  queryKey: dividendKeys.suggestions(accountId),
  pageSize: dividendPageSize,
  getRowId: (entry) => entry.id,
  keepPreviousRows: false,
  fetchPage: async (page, { pageSize, signal }) =>
    unwrap(
      await api.GET('/api/accounts/{id}/dividends/suggestions', {
        params: { path: { id: accountId }, query: { page, pageSize } },
        signal,
      }),
    ),
});

/** The holdings of one account (sold ones too: a dividend may be for a holding long since closed) that match what is typed. */
export const dividendHoldingsQuery = (accountId: string, text: string) =>
  queryOptions({
    queryKey: [...portfolioKeys.lookups, 'dividend-holdings', accountId, text.trim()] as const,
    queryFn: async ({ signal }): Promise<HoldingRow[]> =>
      unwrap(
        await api.GET('/api/holdings', {
          params: {
            query: { accountId, q: text.trim() || undefined, status: 'all', sort: 'symbol', page: 1, pageSize: 20 },
          },
          signal,
        }),
      ).items,
    placeholderData: keepPreviousData,
  });
