import { queryOptions, type QueryClient } from '@tanstack/react-query';
import { api, unwrap, type HoldingRow } from '@/shared/api';
import type { GroupBy } from '../lib/filters';
import { createLimiter, type Limiter } from '../lib/limiter';
import { portfolioKeys } from './portfolioQueries';

/** The live feed's state over REST: the fallback of the hub's `feedStatus`, read while the hub is not connected. */
export const liveStatusQuery = queryOptions({
  queryKey: [...portfolioKeys.live, 'status'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/live/status', { signal })),
});

const pageSize = 100;

/**
 * At most this many requests of one live read are in flight (the per-account fan-out and the pages of each account share it).
 * The read is heavy for the API (every page works out the XIRR of its rows), so it is spread rather than fired at once.
 */
export const liveReadConcurrency = 4;

/**
 * A live read is fresh for as long as the 5-minute timer runs: showing the page again, a window focus or the hub coming back
 * does not read it again (only a real change - sync, trade, import - or the timer does).
 */
const liveStaleMs = 5 * 60_000;

interface HoldingsFilter {
  accountId?: string;
  bucketId?: string;
  groupBy?: GroupBy;
  groupKey?: string;
}

/** Every open holding that matches, in pages of 100 (the API maximum): the first page says how many there are, then the rest, a few at a time. */
async function readAll(filter: HoldingsFilter, signal: AbortSignal, limit: Limiter): Promise<HoldingRow[]> {
  const page = (number: number) =>
    limit(async () =>
      unwrap(
        await api.GET('/api/holdings', {
          params: { query: { ...filter, sort: 'symbol', page: number, pageSize } },
          signal,
        }),
      ),
    );
  const first = await page(1);
  const more = Array.from({ length: Math.max(0, Math.ceil(first.total / pageSize) - 1) }, (_, index) =>
    page(index + 2),
  );
  return [...first.items, ...(await Promise.all(more)).flatMap((next) => next.items)];
}

const unique = (rows: readonly HoldingRow[]): HoldingRow[] => [...new Map(rows.map((row) => [row.id, row])).values()];

/** What the totals are made of: the page's account and bucket, and the accounts of the screen (for "all accounts"). */
export interface LiveBaseScope {
  account: string | null;
  bucket: string | null;
  /** Ids of the accounts with the Portfolio role. */
  accounts: readonly string[];
}

/**
 * Every open holding the tiles are made of, so that the browser can work the totals out again at a pushed price. With an
 * account that is the account's holdings. Without one the summary counts only accounts that count in totals, which the
 * accounts list does not say; the holdings list answers that for a group (`groupBy=account`: "without an account only
 * accounts that count in totals are listed"), so each account is asked as the group it is, and one that does not count
 * gives nothing.
 */
export const liveBaseQuery = (scope: LiveBaseScope) => {
  const bucketId = scope.bucket ?? undefined;
  return queryOptions({
    staleTime: liveStaleMs,
    queryKey: [
      ...portfolioKeys.holdings,
      'live-base',
      { account: scope.account, bucket: scope.bucket, accounts: scope.accounts },
    ] as const,
    queryFn: async ({ signal }) => {
      const limit = createLimiter(liveReadConcurrency);
      if (scope.account !== null) return readAll({ accountId: scope.account, bucketId }, signal, limit);
      const lists = await Promise.all(
        scope.accounts.map((id) => readAll({ groupBy: 'account', groupKey: id, bucketId }, signal, limit)),
      );
      return unique(lists.flat());
    },
  });
};

/** The scope of the group lists: as the page's, and the groups of the response. */
export interface LiveMembersScope {
  account: string | null;
  bucket: string | null;
  by: GroupBy;
  /** The group keys of the groups response. */
  keys: readonly string[];
}

/**
 * Who is in each sector or basket: the ids of the open holdings per group key. The rows do not say their sector or baskets,
 * so each group is asked for its holdings (a few requests at a time).
 */
export const liveMembersQuery = (scope: LiveMembersScope) => {
  const keys = [...scope.keys].sort();
  return queryOptions({
    staleTime: liveStaleMs,
    queryKey: [...portfolioKeys.groups, 'live-members', { ...scope, keys }] as const,
    queryFn: async ({ signal }): Promise<[string, string[]][]> => {
      const limit = createLimiter(liveReadConcurrency);
      return Promise.all(
        keys.map(async (key): Promise<[string, string[]]> => {
          const rows = await readAll(
            {
              accountId: scope.account ?? undefined,
              bucketId: scope.bucket ?? undefined,
              groupBy: scope.by,
              groupKey: key,
            },
            signal,
            limit,
          );
          return [key, rows.map((row) => row.id)];
        }),
      );
    },
  });
};

const liveReadKinds = new Set<unknown>(['live-base', 'live-members']);

/** Whether a query is one of the heavy live reads (every open holding of the scope, or the members of every group). */
export const isLiveRead = (query: { queryKey: readonly unknown[] }): boolean => liveReadKinds.has(query.queryKey[2]);

/** When the holdings behind the live totals were last read (ms; the oldest of the scopes held); null when they are not read. */
export function liveBaseReadAt(queryClient: QueryClient): number | null {
  const times = queryClient
    .getQueryCache()
    .findAll({ queryKey: portfolioKeys.holdings, predicate: (query) => query.queryKey[2] === 'live-base' })
    .map((query) => query.state.dataUpdatedAt)
    .filter((at) => at > 0);
  return times.length === 0 ? null : Math.min(...times);
}
