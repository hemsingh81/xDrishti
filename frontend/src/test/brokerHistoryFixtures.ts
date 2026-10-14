import { http, HttpResponse } from 'msw';
import type {
  BrokerHistoryStatus,
  BrokerKindStatus,
  BrokerSyncCounts,
  BrokerSyncRun,
  LedgerClassTotal,
  LedgerEntry,
  LedgerPage,
} from '@/shared/api';
import { ids } from './fixtures';

const uuid = (n: number) => `00000000-0000-4000-8000-0000000009${String(n).padStart(2, '0')}`;

export const brokerRunIds = { trades: uuid(1), ledger: uuid(2), trades2: uuid(3), ledger2: uuid(4) } as const;

export const buildBrokerCounts = (overrides: Partial<BrokerSyncCounts> = {}): BrokerSyncCounts => ({
  windowsDone: 3,
  windowsTotal: 3,
  halved: 0,
  read: 14,
  stored: 4,
  duplicates: 10,
  dropped: 0,
  enriched: 0,
  skipped: 0,
  skippedByReason: {},
  rebuilt: 0,
  unchanged: 0,
  needsReview: 0,
  notRebuiltTotal: 0,
  notRebuilt: [],
  ...overrides,
});

/** A finished run of one kind that read 11 Oct 2026 and found 4 new rows. */
export const buildBrokerRun = (overrides: Partial<BrokerSyncRun> = {}): BrokerSyncRun => ({
  id: brokerRunIds.trades,
  accountId: ids.main,
  kind: 'trades',
  trigger: 'manual',
  status: 'succeeded',
  from: '2026-09-11',
  to: '2026-10-11',
  startedAt: '2026-10-11T09:54:50Z',
  finishedAt: '2026-10-11T09:55:00Z',
  counts: buildBrokerCounts(),
  error: null,
  ...overrides,
});

export const buildBrokerKind = (overrides: Partial<BrokerKindStatus> = {}): BrokerKindStatus => ({
  watermark: '2026-10-11',
  lastRun: buildBrokerRun(),
  windowsDone: 3,
  windowsTotal: 3,
  ...overrides,
});

/** Both kinds read up to 11 Oct 2026, nothing running, nothing rebuilt, the next read planned. */
export const buildBrokerStatus = (overrides: Partial<BrokerHistoryStatus> = {}): BrokerHistoryStatus => ({
  running: false,
  trades: buildBrokerKind(),
  ledger: buildBrokerKind({
    lastRun: buildBrokerRun({ id: brokerRunIds.ledger, kind: 'ledger', counts: buildBrokerCounts({ stored: 7 }) }),
  }),
  nextRunAt: '2026-10-12T11:00:00Z',
  holdingsRebuilt: 0,
  holdingsNeedingReview: 0,
  needsReviewSymbols: [],
  ...overrides,
});

/** Nothing was ever read. */
export const buildNeverReadStatus = (): BrokerHistoryStatus =>
  buildBrokerStatus({
    trades: buildBrokerKind({ watermark: null, lastRun: null, windowsDone: 0, windowsTotal: 0 }),
    ledger: buildBrokerKind({ watermark: null, lastRun: null, windowsDone: 0, windowsTotal: 0 }),
    nextRunAt: null,
  });

/** A read in progress: trades at window 3 of 12, the ledger not yet started. */
export const buildRunningStatus = (): BrokerHistoryStatus =>
  buildBrokerStatus({
    running: true,
    trades: buildBrokerKind({
      lastRun: buildBrokerRun({
        status: 'running',
        finishedAt: null,
        counts: buildBrokerCounts({ windowsDone: 3, windowsTotal: 12 }),
      }),
      windowsDone: 3,
      windowsTotal: 12,
    }),
  });

export const ledgerIds = (n: number) => `00000000-0000-4000-8000-0000000010${String(n).padStart(2, '0')}`;

export const buildLedgerEntry = (overrides: Partial<LedgerEntry> = {}): LedgerEntry => ({
  id: ledgerIds(1),
  date: '2026-10-09',
  voucherNo: 'V1001',
  description: 'Dividend credited',
  narration: 'Final dividend INFY 21',
  exchange: 'NSE',
  amount: 840,
  class: 'dividend',
  runningBalance: 25_840,
  ...overrides,
});

/** Five entries, one or two of each class, newest first. */
export const sampleLedger: readonly LedgerEntry[] = [
  buildLedgerEntry(),
  buildLedgerEntry({
    id: ledgerIds(2),
    date: '2026-10-08',
    voucherNo: 'V1000',
    description: 'Bank payin',
    narration: 'UPI payin',
    exchange: null,
    amount: 20_000,
    class: 'funds_in',
    runningBalance: 25_000,
  }),
  buildLedgerEntry({
    id: ledgerIds(3),
    date: '2026-10-07',
    voucherNo: 'V0999',
    description: 'DP charges',
    narration: 'DP charges for sale of TCS',
    exchange: null,
    amount: -15.93,
    class: 'charges',
    runningBalance: 5_000,
  }),
  buildLedgerEntry({
    id: ledgerIds(4),
    date: '2026-10-06',
    voucherNo: 'V0998',
    description: 'Payout',
    narration: '<b>Withdrawal</b> to bank',
    exchange: null,
    amount: -3_000,
    class: 'funds_out',
    runningBalance: 5_016,
  }),
  buildLedgerEntry({
    id: ledgerIds(5),
    date: '2026-10-05',
    voucherNo: 'V0997',
    description: 'Journal',
    narration: '',
    exchange: null,
    amount: 1,
    class: 'other',
    runningBalance: 8_016,
  }),
];

/** The totals of all five classes over `entries`. */
export const totalsOfEntries = (entries: readonly LedgerEntry[]): LedgerClassTotal[] =>
  ['funds_in', 'funds_out', 'dividend', 'charges', 'other'].map((cls) => {
    const mine = entries.filter((e) => e.class === cls);
    return { class: cls, count: mine.length, amount: mine.reduce((sum, e) => sum + e.amount, 0) };
  });

/** The ledger as the API answers it: the class filter narrows the items, the totals follow the period only. */
export function answerLedger(
  entries: readonly LedgerEntry[],
  params: URLSearchParams,
  coverageFrom: string | null = entries.reduce<string | null>(
    (first, e) => (first === null || e.date < first ? e.date : first),
    null,
  ),
): LedgerPage {
  const from = params.get('from');
  const to = params.get('to');
  const cls = params.get('class');
  const page = Number(params.get('page') ?? '1');
  const pageSize = Number(params.get('pageSize') ?? '50');
  const inPeriod = entries.filter((e) => (!from || e.date >= from) && (!to || e.date <= to));
  const matching = inPeriod.filter((e) => !cls || e.class === cls);
  return {
    items: matching.slice((page - 1) * pageSize, page * pageSize),
    page,
    pageSize,
    total: matching.length,
    totals: totalsOfEntries(inPeriod),
    coverageFrom,
  };
}

/** Default API mocks: both kinds read, nothing running; an empty ledger. Tests of the sync and the panel replace them. */
export const brokerHistoryHandlers = [
  http.get('/api/accounts/:id/sync/broker-history/status', () => HttpResponse.json(buildBrokerStatus())),
  http.get('/api/accounts/:id/ledger', ({ request }) =>
    HttpResponse.json(answerLedger([], new URL(request.url).searchParams)),
  ),
];
