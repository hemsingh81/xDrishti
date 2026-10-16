import { http, HttpResponse } from 'msw';
import type {
  HoldingRebuildResult,
  Trade,
  TradeBatch,
  TradeBatchPage,
  TradeImportResult,
  TradePage,
  TradePreviewReport,
  TradeRebuild,
  TradeSecurity,
  TradeUndoResult,
} from '@/shared/api';
import { holdingIds, ids } from './fixtures';

const uuid = (kind: number, n: number) =>
  `00000000-0000-4000-8000-00000000${String(kind)}${String(n).padStart(3, '0')}`;

export const tradeIds = { first: uuid(7, 1), second: uuid(7, 2), third: uuid(7, 3), fourth: uuid(7, 4) } as const;
export const batchIds = { first: uuid(8, 1), second: uuid(8, 2), third: uuid(8, 3) } as const;

/** A buy of 10 INFY on 5 Oct 2026 from a CSV import. */
export const buildTrade = (overrides: Partial<Trade> = {}): Trade => ({
  id: tradeIds.first,
  accountId: ids.main,
  symbol: 'INFY',
  isin: 'INE009A01021',
  exchange: 'NSE',
  side: 'buy',
  tradeDate: '2026-10-05',
  tradeTime: null,
  qty: 10,
  price: 1_750,
  charges: 12.5,
  source: 'csv',
  batchId: batchIds.first,
  createdBy: 'owner',
  createdAt: '2026-10-12T09:30:00Z',
  voided: false,
  voidedAt: null,
  voidedBy: null,
  ...overrides,
});

export const buildTradePage = (items: readonly Trade[], overrides: Partial<TradePage> = {}): TradePage => ({
  items: [...items],
  page: 1,
  pageSize: 50,
  total: items.length,
  ...overrides,
});

/** The batch of a file of 12 valid trades, 10 stored (2 were there), 1 row skipped and 1 bad. */
export const buildBatch = (overrides: Partial<TradeBatch> = {}): TradeBatch => ({
  id: batchIds.first,
  accountId: ids.main,
  accountName: 'Test Dhan Main',
  fileName: 'tradebook-fy26.csv',
  sha256: 'a'.repeat(64),
  parsed: 12,
  imported: 10,
  duplicates: 2,
  skipped: 1,
  bad: 1,
  status: 'imported',
  activeTrades: 10,
  securities: 3,
  createdBy: 'owner',
  createdAt: '2026-10-12T09:30:00Z',
  undoneAt: null,
  ...overrides,
});

export const buildBatchPage = (
  items: readonly TradeBatch[],
  overrides: Partial<TradeBatchPage> = {},
): TradeBatchPage => ({
  items: [...items],
  page: 1,
  pageSize: 50,
  total: items.length,
  ...overrides,
});

/** A rebuild that changes the lots: 1 → 3, no hand-dated lot replaced. Override what the test is about. */
export const buildRebuild = (overrides: Partial<TradeRebuild> = {}): TradeRebuild => ({
  status: 'ok',
  reason: null,
  lotsBefore: 1,
  lotsAfter: 3,
  handDatedLotsReplaced: 0,
  syncLotsReplaced: 0,
  realisedPnl: 1_250,
  adjustedFor: 0,
  q: 25,
  net: 15,
  openingQty: { before: 25, after: 10 },
  digest: 'rebuild-digest-1',
  openCostAfter: 40_000,
  brokerCost: 40_000,
  costDelta: 0,
  costNote: null,
  ...overrides,
});

/** The security of the file that the account holds (INFY by default) with an OK rebuild. */
export const buildSecurity = (overrides: Partial<TradeSecurity> = {}): TradeSecurity => ({
  symbol: 'INFY',
  isin: 'INE009A01021',
  matched: true,
  holdingId: holdingIds.infy,
  trades: 5,
  buys: 3,
  sells: 2,
  firstTradeDate: '2024-06-10',
  lastTradeDate: '2026-10-05',
  rebuild: buildRebuild(),
  ...overrides,
});

/** A file of 12 rows: 10 valid trades in two securities (INFY changes, TCS is unchanged), 1 derivative skipped, 1 bad row. */
export const buildReport = (overrides: Partial<TradePreviewReport> = {}): TradePreviewReport => ({
  file: {
    fileName: 'tradebook-fy26.csv',
    rows: 12,
    parsed: 10,
    skipped: { total: 1, byReason: { derivatives: 1 } },
    bad: [{ line: 7, reason: 'The date 31/02/2026 is not a real day.' }],
    badTotal: 1,
    duplicates: 0,
    alreadyImported: 0,
    newTrades: 10,
  },
  securities: [
    buildSecurity(),
    buildSecurity({
      symbol: 'TCS',
      isin: 'INE467B01029',
      holdingId: holdingIds.tcs,
      trades: 5,
      buys: 5,
      sells: 0,
      rebuild: buildRebuild({
        status: 'unchanged',
        lotsBefore: 1,
        lotsAfter: 1,
        realisedPnl: 0,
        openingQty: { before: 0, after: 0 },
      }),
    }),
  ],
  notHeld: { securities: 0, trades: 0 },
  unmatched: { securities: 0, trades: 0 },
  digest: 'digest-1',
  securitiesTotal: 2,
  unmatchedListed: 0,
  ...overrides,
});

export const buildImportResult = (overrides: Partial<TradeImportResult> = {}): TradeImportResult => ({
  batch: buildBatch(),
  imported: 10,
  alreadyImported: 0,
  rebuilt: true,
  holdingsRebuilt: 1,
  lotsReplaced: 1,
  report: buildReport(),
  ...overrides,
});

export const buildUndoResult = (overrides: Partial<TradeUndoResult> = {}): TradeUndoResult => ({
  batch: buildBatch({ status: 'undone', activeTrades: 0, undoneAt: '2026-10-13T08:00:00Z' }),
  alreadyUndone: false,
  voided: 10,
  holdingsRebuilt: 1,
  lotsReplaced: 3,
  holdingsNotRebuilt: 0,
  dryRun: false,
  applied: true,
  handDatedLotsReplaced: 0,
  syncLotsReplaced: 0,
  notRebuilt: [],
  ...overrides,
});

export const buildRebuildResult = (
  rebuild: TradeRebuild = buildRebuild(),
  overrides: Partial<HoldingRebuildResult> = {},
): HoldingRebuildResult => ({ dryRun: true, applied: false, rebuild, ...overrides });

/** Default API mocks: no imports, no trades, and a rebuild that changes nothing. The trade fake replaces them. */
export const tradeHandlers = [
  http.get('/api/portfolio/trade-batches', () => HttpResponse.json(buildBatchPage([]))),
  http.get('/api/portfolio/cost-batches', () => HttpResponse.json({ items: [], page: 1, pageSize: 50, total: 0 })),
  http.get('/api/holdings/:id/trades', () => HttpResponse.json(buildTradePage([]))),
  http.post('/api/holdings/:id/rebuild-lots', () =>
    HttpResponse.json(
      buildRebuildResult(buildRebuild({ status: 'unchanged', lotsBefore: 1, lotsAfter: 1, realisedPnl: 0 })),
    ),
  ),
];
