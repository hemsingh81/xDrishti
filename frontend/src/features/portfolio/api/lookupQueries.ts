import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import { api, unwrap, type HoldingRow, type InstrumentSummary } from '@/shared/api';
import { portfolioKeys } from './portfolioQueries';

export interface HoldingRef {
  id: string;
  accountId: string;
  symbol: string;
}

/** Pages of 100 (the API maximum) read at most this many times looking for a holding: a symbol rarely matches more. */
const maxLookupPages = 5;

/** The first open holding of the search that `matches`, reading the pages in order; null when none does. */
async function findHolding(
  search: { accountId?: string; q: string },
  matches: (holding: HoldingRow) => boolean,
  signal: AbortSignal,
): Promise<HoldingRow | null> {
  for (let page = 1; page <= maxLookupPages; page += 1) {
    const result = unwrap(
      await api.GET('/api/holdings', {
        params: { query: { ...search, status: 'open', sort: 'symbol', page, pageSize: 100 } },
        signal,
      }),
    );
    const found = result.items.find(matches);
    if (found) return found;
    if (result.items.length === 0 || page * result.pageSize >= result.total) return null;
  }
  return null;
}

/**
 * One open holding, found by searching its account for its symbol (there is no read by id): the Action center knows the
 * holding by id, the drawer and the alert dialog want its figures (and its last price). Further pages are read when the
 * symbol matches more than 100 holdings. Null when it is not among the matches (closed since, or renamed).
 */
export const holdingLookupQuery = (ref: HoldingRef) =>
  queryOptions({
    queryKey: [...portfolioKeys.lookups, 'holding', ref.id] as const,
    queryFn: ({ signal }) => findHolding({ accountId: ref.accountId, q: ref.symbol }, (h) => h.id === ref.id, signal),
  });

/** The symbol of an instrument without its exchange ("NSE:TCS" -> "TCS"), as the holdings carry it. */
export const bareSymbol = (symbol: string): string => symbol.split(':').at(-1) ?? symbol;

/**
 * A priced open holding of an instrument in any account, for the price of an alert set on a picked instrument (the API
 * prefers a held instrument's last price to its last stored close). Null when it is not held.
 */
export const heldInstrumentQuery = (instrument: { id: string; symbol: string }) =>
  queryOptions({
    queryKey: [...portfolioKeys.lookups, 'held', instrument.id] as const,
    queryFn: ({ signal }) =>
      findHolding({ q: bareSymbol(instrument.symbol) }, (h) => h.instrumentId === instrument.id && h.priced, signal),
  });

export interface LastClose {
  price: number;
  /** The bar's time (ISO). */
  at: string;
}

/** The last stored daily close of an instrument; null when none is stored (it is not tracked, or has no history yet). */
export const lastCloseQuery = (instrumentId: string) =>
  queryOptions({
    queryKey: [...portfolioKeys.lookups, 'close', instrumentId] as const,
    queryFn: async ({ signal }): Promise<LastClose | null> => {
      const bars = unwrap(
        await api.GET('/api/data/instruments/{id}/bars', {
          params: { path: { id: instrumentId }, query: { timeframe: '1D', take: 1 } },
          signal,
        }),
      );
      const latest = bars.items[0];
      return latest ? { price: latest.close, at: latest.ts } : null;
    },
  });

/** Alerts are for NSE shares and ETFs: the picker searches those two types (the API takes one type per call) and merges them. */
export const alertInstrumentsQuery = (text: string) =>
  queryOptions({
    queryKey: [...portfolioKeys.lookups, 'instruments', text.trim()] as const,
    queryFn: async ({ signal }): Promise<InstrumentSummary[]> => {
      const search = async (type: 'share' | 'etf') =>
        unwrap(
          await api.GET('/api/instruments', {
            params: { query: { q: text.trim() || undefined, type, sort: 'symbol', page: 1, pageSize: 20 } },
            signal,
          }),
        ).items;
      const [shares, etfs] = await Promise.all([search('share'), search('etf')]);
      return [...shares, ...etfs].sort((a, b) => a.symbol.localeCompare(b.symbol)).slice(0, 20);
    },
    placeholderData: keepPreviousData,
  });
