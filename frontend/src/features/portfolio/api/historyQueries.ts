import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';
import { portfolioKeys } from './portfolioQueries';

export interface HistoryRequest {
  account: string | null;
  bucket: string | null;
  /** yyyy-MM-dd; left out = from the first day of the history. */
  from?: string;
  /** yyyy-MM-dd; left out = up to the last day. */
  to?: string;
}

/** The value history of the account and bucket in the scope bar, over a range. The resolution is left to the server. */
export const historyQuery = ({ account, bucket, from, to }: HistoryRequest) => {
  const query = { accountId: account ?? undefined, bucketId: bucket ?? undefined, from, to };
  return queryOptions({
    queryKey: [...portfolioKeys.history, query] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/portfolio/history', { params: { query }, signal })),
    // A new range or scope keeps the old chart (dimmed) until its data arrives, so the card does not jump.
    placeholderData: keepPreviousData,
  });
};

/** The benchmark index in force (Nifty 50 until another is chosen). */
export const benchmarkQuery = queryOptions({
  queryKey: portfolioKeys.benchmark,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/portfolio/benchmark', { signal })),
});
