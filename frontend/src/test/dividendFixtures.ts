import { http, HttpResponse } from 'msw';
import type {
  DividendPage,
  DividendSuggestionPage,
  DividendView,
  HoldingGroup,
  HoldingRow,
  HoldingsSummary,
  IncomeView,
  LedgerEntry,
} from '@/shared/api';
import { buildGroup } from './groupFixtures';
import { holdingIds, ids } from './fixtures';

const uuid = (n: number) => `00000000-0000-4000-8000-0000000012${String(n).padStart(2, '0')}`;

export const dividendIds = { first: uuid(1), second: uuid(2), third: uuid(3) } as const;

/** A dividend of ₹1,200 on RELIANCE paid on 5 Dec 2025, entered by hand. */
export const buildDividend = (overrides: Partial<DividendView> = {}): DividendView => ({
  id: dividendIds.first,
  accountId: ids.main,
  holdingId: holdingIds.reliance,
  symbol: 'RELIANCE',
  isin: 'INE002A01018',
  payDate: '2025-12-05',
  amount: 1_200,
  perShare: 30,
  note: null,
  source: 'manual',
  createdBy: 'owner',
  createdAt: '2026-10-12T09:30:00Z',
  voided: false,
  voidedAt: null,
  voidedBy: null,
  ...overrides,
});

export const buildDividendPage = (
  items: readonly DividendView[],
  overrides: Partial<DividendPage> = {},
): DividendPage => ({
  items: [...items],
  page: 1,
  pageSize: 50,
  total: items.length,
  ...overrides,
});

export const buildSuggestionPage = (
  items: readonly LedgerEntry[],
  overrides: Partial<DividendSuggestionPage> = {},
): DividendSuggestionPage => ({
  items: [...items],
  page: 1,
  pageSize: 50,
  total: items.length,
  ...overrides,
});

/**
 * The income of a scope with dividends and charges: ₹1,200 of dividends entered from 5 Dec 2025, ₹150 of trade charges
 * stored from 2 Jan 2025 (no account costs), the net figures counted from 5 Dec 2025 (the later start of the two sources),
 * a total return of 17.2 % and 17.05 % net of costs, a net XIRR of 13.8 %.
 */
export const buildIncome = (overrides: Partial<IncomeView> = {}): IncomeView => ({
  dividends: 1_200,
  charges: 150,
  ledgerCharges: null,
  unallocated: null,
  totalReturnPct: 17.2,
  netTotalReturnPct: 17.05,
  netXirr: 13.8,
  netXirrState: 'ok',
  coverageFrom: '2025-12-05',
  dividendsFrom: '2025-12-05',
  chargesFrom: '2025-01-02',
  tradeChargesFrom: '2025-01-02',
  ledgerChargesFrom: null,
  ...overrides,
});

/** A holding row with income. */
export const withIncome = (row: HoldingRow, income: IncomeView | null = buildIncome()): HoldingRow => ({
  ...row,
  income,
});

export const summaryWithIncome = (
  summary: HoldingsSummary,
  income: IncomeView | null = buildIncome({ unallocated: 200 }),
): HoldingsSummary => ({ ...summary, income });

/** The Unallocated row of a grouping: dividends and costs of no group, no holdings, no value. */
export const buildUnallocatedGroup = (overrides: Partial<HoldingGroup> = {}): HoldingGroup =>
  buildGroup({
    key: '~unallocated',
    name: 'Unallocated',
    holdings: 0,
    unpriced: 0,
    invested: 0,
    value: null,
    pnl: null,
    pnlPct: null,
    xirr: null,
    xirrState: 'no_data',
    dayChange: null,
    dayChangePct: null,
    dayChangeExcluded: 0,
    weightPct: null,
    income: buildIncome({
      dividends: 200,
      charges: 25,
      ledgerCharges: 25,
      unallocated: 200,
      totalReturnPct: null,
      netTotalReturnPct: null,
      netXirr: null,
      netXirrState: 'no_data',
      dividendsFrom: '2026-04-04',
      chargesFrom: '2026-01-05',
      tradeChargesFrom: null,
      ledgerChargesFrom: '2026-01-05',
      coverageFrom: '2026-04-04',
    }),
    ...overrides,
  });

/** Default API mocks: nothing entered, nothing suggested. Tests of the dividends bring `createDividendFake`. */
export const dividendHandlers = [
  http.get('/api/holdings/:id/dividends', () => HttpResponse.json(buildDividendPage([]))),
  http.get('/api/accounts/:id/dividends/suggestions', () => HttpResponse.json(buildSuggestionPage([]))),
  http.get('/api/accounts/:id/dividends', () => HttpResponse.json(buildDividendPage([]))),
];
