import { http, HttpResponse } from 'msw';
import type { HoldingRow } from '@/shared/api';
import { buildSummary, ids, searchHoldings } from './fixtures';
import { instrumentOf } from './hubFake';
import { server } from './server';
import { groupsOf, rowOf, summaryOf, type ModelDimension, type ModelHolding } from './serverModel';

/** 10:35:44 in India on Wednesday 14 Oct 2026 (a trading day): the page is read at this moment. */
export const liveNow = '2026-10-14T10:35:44+05:30';
export const at = (time: string, day = '2026-10-14') => Date.parse(`${day}T${time}+05:30`);
export const isoAt = (time: string, day = '2026-10-14') => new Date(at(time, day)).toISOString();

const holdingOf = (n: number, symbol: string, overrides: Partial<ModelHolding>): ModelHolding => ({
  id: `00000000-0000-4000-8000-0000000c${String(n).padStart(4, '0')}`,
  symbol,
  accountId: ids.main,
  bucketId: '00000000-0000-4000-8000-0000000000b1',
  sector: n === 3 ? 'Unclassified' : 'IT',
  instrumentId: instrumentOf(n),
  qty: 10,
  status: 'open',
  lots: [{ qty: 10, unitCostPaise: 250_000, closed: false, closePricePaise: 0 }],
  pricePaise: 290_000,
  priceAt: at('10:30:00'),
  source: 'quote',
  previousClosePaise: 288_000,
  previousCloseAt: at('10:30:00'),
  ...overrides,
});

/**
 * Three holdings of the main account, read at 10:35:44: RELIANCE at a quote of 10:30, TCS at a live tick of 10:35:30, and
 * SMALLCO, which is not in the master list (no instrument: it can never be live) at yesterday's sync.
 */
export const liveHoldings = (): ModelHolding[] => [
  holdingOf(1, 'RELIANCE', {}),
  holdingOf(2, 'TCS', {
    lots: [{ qty: 10, unitCostPaise: 400_000, closed: false, closePricePaise: 0 }],
    pricePaise: 388_000,
    priceAt: at('10:35:30'),
    source: 'live',
    previousClosePaise: 390_000,
    previousCloseAt: at('10:30:00'),
  }),
  holdingOf(3, 'SMALLCO', {
    instrumentId: null,
    qty: 100,
    lots: [{ qty: 100, unitCostPaise: 5_000, closed: false, closePricePaise: 0 }],
    pricePaise: 6_000,
    priceAt: at('15:25:00', '2026-10-13'),
    source: 'sync',
    previousClosePaise: 5_800,
    previousCloseAt: null,
  }),
];

export interface LiveServer {
  /** The holdings the API answers with; replace to change what a re-read returns. */
  holdings: ModelHolding[];
  loads: { list: number; summary: number; groups: number; base: number };
  /** The `GET /api/holdings` requests, with their query. */
  lists: URLSearchParams[];
}

/** Serves the holdings list, the totals and the groups from a model of the server, so a re-read answers what the server would. */
export function serveLive(holdings: ModelHolding[] = liveHoldings()): LiveServer {
  const state: LiveServer = { holdings, loads: { list: 0, summary: 0, groups: 0, base: 0 }, lists: [] };
  const rows = (): HoldingRow[] => state.holdings.map(rowOf);
  server.use(
    http.get('/api/holdings', ({ request }) => {
      const params = new URL(request.url).searchParams;
      state.lists.push(params);
      const grouped = params.has('groupBy');
      state.loads[grouped ? 'base' : 'list'] += 1;
      const key = params.get('groupKey');
      const by = params.get('groupBy');
      const list = rows().filter((row) => {
        const model = state.holdings.find((h) => h.id === row.id)!;
        if (by === 'account') return row.accountId === key;
        if (by === 'sector') return model.sector === key;
        return true;
      });
      return HttpResponse.json(searchHoldings(list, params));
    }),
    http.get('/api/holdings/summary', () => {
      state.loads.summary += 1;
      return HttpResponse.json(
        summaryOf(state.holdings, buildSummary({ openCount: 3, closedCount: 0, unmappedCount: 1 })),
      );
    }),
    http.get('/api/holdings/groups', ({ request }) => {
      state.loads.groups += 1;
      const by = new URL(request.url).searchParams.get('by') as ModelDimension;
      return HttpResponse.json(groupsOf(state.holdings, by));
    }),
  );
  return state;
}
