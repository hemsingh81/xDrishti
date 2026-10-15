import type {
  BillDetails,
  BillNameView,
  CostBatch,
  CostBatchPage,
  CostImportResult,
  CostUndoResult,
  DematPreview,
  DpLine,
  DpPreview,
  ImportFilePreview,
  ImportFileResult,
} from '@/shared/api';
import { holdingIds, ids } from './fixtures';
import { buildBatch, buildImportResult, buildReport } from './tradeFixtures';

// Everything here is made up: invented company names, ISINs, files and amounts. No real report is used.

const uuid = (n: number) => `00000000-0000-4000-8000-0000000017${String(n).padStart(2, '0')}`;

export const costBatchIds = { first: uuid(1), second: uuid(2), third: uuid(3) } as const;
export const instrumentIds = { orion: uuid(50), nova: uuid(51) } as const;

export const isins = {
  orion: 'INE0TS001018',
  nova: 'INE0TS002016',
  novaTwo: 'INE0TS003014',
  zenith: 'INE0TS004012',
} as const;

const summaryOf = (overrides: Partial<NonNullable<ImportFilePreview['summary']>> = {}) => ({
  rows: 40,
  valid: 34,
  toAdd: 30,
  alreadyImported: 2,
  skipped: 4,
  bad: 1,
  from: '2025-04-01',
  to: '2026-03-31',
  ...overrides,
});

const blank = (overrides: Partial<ImportFilePreview>): ImportFilePreview => ({
  fileName: 'file.csv',
  kind: 'unknown',
  supported: false,
  routeTo: null,
  message: null,
  error: null,
  summary: null,
  warnings: [],
  trades: null,
  bill: null,
  costs: null,
  demat: null,
  digest: null,
  ...overrides,
});

export const buildBillName = (overrides: Partial<BillNameView> = {}): BillNameView => ({
  name: 'INFOSYS LTD',
  nameKey: 'infosys',
  symbolHint: 'INFY',
  status: 'resolved',
  kind: 'symbol',
  isin: 'INE009A01021',
  symbol: 'INFY',
  securityName: 'Infosys',
  holdingId: holdingIds.infy,
  instrumentId: null,
  rows: 12,
  trades: 14,
  candidates: [],
  ...overrides,
});

/** Two resolved names (one by part of the name), the two spellings of one name nothing fits, and an ambiguous one. */
export const buildBill = (overrides: Partial<BillDetails> = {}): BillDetails => ({
  names: [
    buildBillName(),
    buildBillName({
      name: 'TATA CONSULTANCY SERV LTD',
      nameKey: 'tata consultancy serv',
      symbolHint: null,
      kind: 'partial',
      isin: 'INE467B01029',
      symbol: 'TCS',
      securityName: 'Tata Consultancy Services',
      holdingId: holdingIds.tcs,
      rows: 4,
      trades: 4,
    }),
    buildBillName({
      name: 'ORION STEEL LTD.',
      nameKey: 'orion steel',
      symbolHint: null,
      status: 'needs_match',
      kind: 'none',
      isin: null,
      symbol: null,
      securityName: null,
      holdingId: null,
      rows: 3,
      trades: 3,
    }),
    buildBillName({
      name: 'Orion Steel',
      nameKey: 'orion steel',
      symbolHint: null,
      status: 'needs_match',
      kind: 'none',
      isin: null,
      symbol: null,
      securityName: null,
      holdingId: null,
      rows: 2,
      trades: 2,
    }),
    buildBillName({
      name: 'NOVA OXIDES & CHEM LTD',
      nameKey: 'nova oxides chem',
      symbolHint: null,
      status: 'ambiguous',
      kind: 'none',
      isin: null,
      symbol: null,
      securityName: null,
      holdingId: null,
      rows: 1,
      trades: 1,
      candidates: [
        {
          isin: isins.nova,
          symbol: 'NOVAOX',
          name: 'Nova Oxides and Chemicals',
          holdingId: holdingIds.smallco,
          instrumentId: null,
        },
        {
          isin: isins.novaTwo,
          symbol: 'NOVAOY',
          name: 'Nova Oxide Industries',
          holdingId: null,
          instrumentId: instrumentIds.nova,
        },
      ],
    }),
  ],
  namesTotal: 5,
  needsMatch: 3,
  needsMatchTrades: 6,
  coverage: {
    covered: 4,
    differences: [{ date: '2025-06-12', isin: 'INE009A01021', symbol: 'INFY', side: 'buy', billQty: 12, dhanQty: 10 }],
    differencesTotal: 1,
  },
  charges: { total: 1_234.56, reportTotal: 1_234.56, matches: true },
  ...overrides,
});

/** A global transaction report: 30 new trades in two securities, 4 rows left out for three reasons, five names. */
export const buildGtrPreview = (overrides: Partial<ImportFilePreview> = {}): ImportFilePreview =>
  blank({
    fileName: 'global-transaction-fy26.csv',
    kind: 'global_transaction_report',
    supported: true,
    summary: summaryOf(),
    warnings: [
      { code: 'needs_match', message: 'x', count: 3 },
      { code: 'covered_by_dhan', message: 'x', count: 4 },
    ],
    trades: buildReport({
      file: {
        fileName: 'global-transaction-fy26.csv',
        rows: 40,
        parsed: 34,
        skipped: { total: 8, byReason: { derivatives: 3, commodity: 1, covered_by_dhan: 4 } },
        bad: [{ line: 9, reason: 'The date 31/02/2026 is not a real day.' }],
        badTotal: 1,
        duplicates: 0,
        alreadyImported: 2,
        newTrades: 30,
      },
      digest: 'gtr-digest-1',
    }),
    bill: buildBill(),
    digest: 'gtr-digest-1',
    ...overrides,
  });

export const buildDpLine = (overrides: Partial<DpLine> = {}): DpLine => ({
  line: 1,
  date: '2025-08-14',
  isin: isins.orion,
  name: 'Orion Steel Ltd',
  quantity: 25,
  side: 'sell',
  type: 'Market',
  charge: 12.5,
  status: 'new',
  ...overrides,
});

export const buildCosts = (overrides: Partial<DpPreview> = {}): DpPreview => ({
  lines: [
    buildDpLine(),
    buildDpLine({ line: 2, date: '2025-08-15', charge: 0, status: 'zero', type: 'DP Fees', side: 'other' }),
    buildDpLine({ line: 3, date: '2025-08-16', side: 'buy', charge: 0, status: 'buy_side' }),
    buildDpLine({ line: 4, date: '2025-08-17', status: 'already_in_ledger' }),
  ],
  linesTotal: 4,
  counts: { read: 4, toStore: 1, zero: 1, buySide: 1, alreadyImported: 0, alreadyInLedger: 1, offMarket: 0 },
  amountToStore: 12.5,
  names: { known: 1, new: 2 },
  ...overrides,
});

/** A depository-charges sheet: 1 of 4 lines is stored (₹12.50). */
export const buildDpPreview = (overrides: Partial<ImportFilePreview> = {}): ImportFilePreview =>
  blank({
    fileName: 'dp-charges-fy26.xls',
    kind: 'dp_charges',
    supported: true,
    summary: summaryOf({ rows: 4, valid: 4, toAdd: 1, alreadyImported: 0, skipped: 3, bad: 0 }),
    costs: buildCosts(),
    digest: 'dp-digest-1',
    ...overrides,
  });

export const buildDemat = (overrides: Partial<DematPreview> = {}): DematPreview => ({
  asOf: '2026-03-31',
  securities: 6,
  matching: 4,
  quantityDifferences: [{ isin: isins.orion, name: 'Orion Steel Ltd', statement: 30, synced: 25 }],
  quantityDifferencesTotal: 1,
  heldNotSynced: [{ isin: isins.zenith, name: 'Zenith Energy System Ltd', quantity: 8 }],
  heldNotSyncedTotal: 1,
  syncedNotInStatement: [{ symbol: 'SMALLCO', isin: null, name: 'Small Co', quantity: 5 }],
  syncedNotInStatementTotal: 1,
  names: { known: 3, new: 3 },
  ...overrides,
});

/** A demat holding statement: it differs from the synced holdings in three ways and changes none of them. */
export const buildDematPreview = (overrides: Partial<ImportFilePreview> = {}): ImportFilePreview =>
  blank({
    fileName: 'demat-holding.csv',
    kind: 'demat_holding',
    supported: true,
    summary: summaryOf({ rows: 6, valid: 6, toAdd: 0, alreadyImported: 0, skipped: 0, bad: 0, from: null, to: null }),
    warnings: [{ code: 'holdings_differ', message: 'x', count: 3 }],
    demat: buildDemat(),
    digest: 'demat-digest-1',
    ...overrides,
  });

/** A kind that is recognised and not imported here (`pnl_report`, `tradebook_without_sides`, `statement`). */
export const buildUnsupportedPreview = (
  kind: string,
  fileName: string,
  message: string | null,
  overrides: Partial<ImportFilePreview> = {},
): ImportFilePreview => blank({ fileName, kind, supported: false, message, ...overrides });

export const buildDividendRoutePreview = (): ImportFilePreview =>
  buildUnsupportedPreview(
    'dividends',
    'dividends-fy26.csv',
    'This is a dividend list: import it with the dividend import.',
    { routeTo: 'dividends' },
  );

export const buildUnknownPreview = (fileName = 'mystery.csv'): ImportFilePreview =>
  buildUnsupportedPreview('unknown', fileName, null, {
    error: {
      code: 'portfolio.wrong_file_kind',
      message:
        'This file is not one of the reports the app reads: accepted are transaction report, DP charges, demat holding, trade list and dividend list.',
    },
  });

export const buildTradeListPreview = (overrides: Partial<ImportFilePreview> = {}): ImportFilePreview =>
  blank({
    fileName: 'tradebook-fy26.csv',
    kind: 'trades',
    supported: true,
    summary: summaryOf({ rows: 12, valid: 10, toAdd: 10, alreadyImported: 0, skipped: 1, bad: 1 }),
    trades: buildReport(),
    digest: 'trades-digest-1',
    ...overrides,
  });

export const buildCostBatch = (overrides: Partial<CostBatch> = {}): CostBatch => ({
  id: costBatchIds.first,
  accountId: ids.main,
  accountName: 'Test Dhan Main',
  fileName: 'dp-charges-fy26.xls',
  sha256: 'c'.repeat(64),
  parsed: 4,
  imported: 1,
  duplicates: 0,
  bad: 0,
  amount: 12.5,
  status: 'imported',
  activeEntries: 1,
  createdBy: 'owner',
  createdAt: '2026-10-12T10:30:00Z',
  undoneAt: null,
  ...overrides,
});

export const buildCostBatchPage = (
  items: readonly CostBatch[],
  overrides: Partial<CostBatchPage> = {},
): CostBatchPage => ({ items: [...items], page: 1, pageSize: 50, total: items.length, ...overrides });

export const buildCostImportResult = (overrides: Partial<CostImportResult> = {}): CostImportResult => ({
  batch: buildCostBatch(),
  imported: 1,
  amount: 12.5,
  alreadyImported: 0,
  alreadyInLedger: 1,
  namesLearned: 2,
  ...overrides,
});

export const buildCostUndoResult = (overrides: Partial<CostUndoResult> = {}): CostUndoResult => ({
  batch: buildCostBatch({ status: 'undone', activeEntries: 0, undoneAt: '2026-10-13T08:00:00Z' }),
  alreadyUndone: false,
  voided: 1,
  ...overrides,
});

export const buildFileResult = (overrides: Partial<ImportFileResult>): ImportFileResult => ({
  fileName: 'file.csv',
  kind: 'unknown',
  status: 'imported',
  message: null,
  error: null,
  preview: null,
  trades: null,
  costs: null,
  demat: null,
  aliasesSaved: 0,
  ...overrides,
});

export { buildBatch, buildImportResult };
