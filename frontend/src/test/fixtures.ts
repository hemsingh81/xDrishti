import type {
  AccountDetail,
  AccountEvent,
  AccountSummary,
  Basket,
  BasketHistoryEntry,
  BasketMember,
  BasketMembers,
  InstrumentBasket,
  BrokerConnection,
  HoldingLots,
  HoldingPage,
  HoldingRow,
  HoldingsSummary,
  FirstImportView,
  PortfolioSaved,
  InstrumentDetail,
  InstrumentFacets,
  InstrumentPage,
  InstrumentSummary,
  LotRow,
  PortfolioBucket,
  Problem,
  RefreshRun,
  RefreshStatus,
  Returns,
  RiskProfile,
  SessionInfo,
  SyncRun,
  SyncStatus,
  SystemStatus,
  TrackedInfo,
} from '@/shared/api';

/** Typed test data builders — tests override only what they care about. */
export function buildSystemStatus(overrides: Partial<SystemStatus> = {}): SystemStatus {
  return {
    application: {
      name: 'xd-api',
      version: '0.1.0',
      environment: 'Test',
      instance: 'test',
      startedAt: '2026-10-08T09:00:00Z',
      uptimeSeconds: 3_660,
    },
    database: {
      isConnected: true,
      serverVersion: '18.6',
      timescaleVersion: '2.30.2',
      latestMigration: '20261008153115_InitialPlatform',
      pendingMigrations: 0,
      latencyMs: 1.4,
      error: null,
    },
    services: [
      {
        name: 'xd-worker',
        instance: 'w1',
        version: '0.1.0',
        startedAt: '2026-10-08T09:00:00Z',
        lastSeenAt: new Date().toISOString(),
        isAlive: true,
      },
    ],
    isHealthy: true,
    checkedAt: '2026-10-08T10:01:00Z',
    ...overrides,
  };
}

/** Test owner. Never a real address: example@invalid.test. */
export function buildSession(overrides: Partial<SessionInfo> = {}): SessionInfo {
  return {
    user: { email: 'example@invalid.test', displayName: 'Test Owner' },
    csrfToken: 'csrf-token-1',
    idleTimeoutSeconds: 1_800,
    expiresAt: '2026-10-10T22:00:00Z',
    ...overrides,
  };
}

export function buildProblem(code: string, status: number, overrides: Partial<Problem> = {}): Problem {
  return { title: code, status, code, ...overrides };
}

// ---- Accounts (all names, ids and client ids are made up) ----------------------------------------------------------

export const ids = {
  main: '00000000-0000-4000-8000-000000000001',
  swing: '00000000-0000-4000-8000-000000000002',
  standard: '00000000-0000-4000-8000-0000000000a1',
  conservative: '00000000-0000-4000-8000-0000000000a2',
} as const;

export function buildRiskProfile(overrides: Partial<RiskProfile> = {}): RiskProfile {
  return {
    id: ids.standard,
    name: 'Standard',
    isSeeded: true,
    accountCount: 1,
    riskPerTradePct: 0.5,
    maxCapitalPerTradePct: 20,
    maxNewEntriesPerDay: 3,
    maxOpenPositions: 5,
    maxTotalRiskPct: 3,
    maxPerSector: 2,
    maxPairCorrelation: 0.8,
    maxIntraday: 3,
    maxSwing: 5,
    dailyLossR: 3,
    dailyLossPct: 1.5,
    weeklyLossR: 6,
    weeklyLossAction: 'halve_size',
    ...overrides,
  };
}

export const buildRiskProfiles = (): RiskProfile[] => [
  buildRiskProfile(),
  buildRiskProfile({
    id: ids.conservative,
    name: 'Conservative',
    accountCount: 0,
    riskPerTradePct: 0.25,
    maxNewEntriesPerDay: 2,
    maxOpenPositions: 3,
    dailyLossPct: 1,
  }),
];

export const buildBucket = (overrides: Partial<PortfolioBucket> = {}): PortfolioBucket => ({
  id: '00000000-0000-4000-8000-0000000000b9',
  name: 'Retirement',
  isAccountDefault: false,
  accountId: null,
  accountName: null,
  ...overrides,
});

/** What the picker gets by default: the default bucket of each account whose Portfolio role is on. No presets. */
export const buildBuckets = (): PortfolioBucket[] => [
  buildBucket({
    id: '00000000-0000-4000-8000-0000000000b1',
    name: 'Test Dhan Main · Portfolio',
    isAccountDefault: true,
    accountId: ids.main,
    accountName: 'Test Dhan Main',
  }),
];

/** The auto refresh a new account starts with: off (16:00 is kept for when the owner turns it on); first import 1 Jan 2025. */
const offRefresh = { refreshMode: 'off', refreshDailyTimeIst: '16:00', firstImportDate: '2025-01-01' } as const;

const hour = 3_600_000;
const fromNow = (ms: number) => new Date(Date.now() + ms).toISOString();

/** Not connected: no token, nothing known about the broker user. */
export function buildConnection(overrides: Partial<BrokerConnection> = {}): BrokerConnection {
  return {
    status: 'not_connected',
    expiresAt: null,
    brokerUserName: null,
    brokerUserId: null,
    lastConnectedAt: null,
    lastError: null,
    ...overrides,
  };
}

/** Connected through the broker's sign-in an hour ago; the token runs for another 6 hours (relative to now). */
export function buildConnected(overrides: Partial<BrokerConnection> = {}): BrokerConnection {
  return buildConnection({
    status: 'connected',
    expiresAt: fromNow(6 * hour),
    brokerUserName: 'TEST OWNER',
    brokerUserId: 'TEST••90',
    lastConnectedAt: fromNow(-hour),
    ...overrides,
  });
}

/**
 * Full account as the API returns it: primary Data, Paper trading on the Standard profile, Portfolio on; connected via
 * the broker's sign-in. Override `connection` and `tokenStatus` together (the API keeps them equal) — see `withConnection`.
 */
export function buildAccountDetail(overrides: Partial<AccountDetail> = {}): AccountDetail {
  return {
    id: ids.main,
    slug: 'test-dhan-main',
    name: 'Test Dhan Main',
    broker: 'dhan',
    maskedClientId: 'TEST••90',
    color: '#5b8def',
    notes: null,
    secrets: [
      { name: 'apiKey', set: true, updatedAt: '2026-10-09T09:00:00Z' },
      { name: 'apiSecret', set: true, updatedAt: '2026-10-09T09:00:00Z' },
    ],
    apiKeyIssuedOn: '2026-10-09',
    apiKeyExpiresOn: '2027-10-09',
    tokenStatus: 'connected',
    connection: buildConnected(),
    createdAt: '2026-10-09T09:00:00Z',
    updatedAt: '2026-10-09T09:00:00Z',
    roles: {
      data: { enabled: true, primary: true, standby: false },
      trading: { enabled: true, mode: 'paper', profile: 'Standard' },
      portfolio: { enabled: true },
    },
    trading: {
      enabled: true,
      mode: 'paper',
      riskProfileId: ids.standard,
      riskProfileName: 'Standard',
      capitalSource: 'fixed',
      capitalAmount: 200_000,
      styles: ['swing'],
      instruments: { intraday: [], swingBuy: ['EQ_CNC'], swingSell: [] },
      overrides: { riskPerTradePct: null, maxNewEntriesPerDay: null, maxOpenPositions: null, dailyLossPct: null },
    },
    portfolio: {
      enabled: true,
      defaultBucket: 'Test Dhan Main · Portfolio',
      autoBucketName: 'Test Dhan Main · Portfolio',
      includeInTotals: true,
      ...offRefresh,
      hasHoldings: true,
    },
    data: { enabled: true, primary: true, standby: false, liveFeedBudget: 200 },
    ...overrides,
  };
}

/**
 * A second account: standby Data, Manual-live trading, no Portfolio (and no portfolio settings yet). It predates the
 * vault, so its secrets are not set, and it is not connected.
 */
export function buildSwingAccount(overrides: Partial<AccountDetail> = {}): AccountDetail {
  return buildAccountDetail({
    id: ids.swing,
    slug: 'test-dhan-swing',
    name: 'Test Dhan Swing',
    maskedClientId: 'DEMO••12',
    color: '#16a34a',
    secrets: [
      { name: 'apiKey', set: false, updatedAt: null },
      { name: 'apiSecret', set: false, updatedAt: null },
    ],
    apiKeyIssuedOn: '2026-10-09',
    apiKeyExpiresOn: '2027-10-09',
    tokenStatus: 'not_connected',
    connection: buildConnection(),
    roles: {
      data: { enabled: true, primary: false, standby: true },
      trading: { enabled: true, mode: 'manual_live', profile: 'Conservative' },
      portfolio: { enabled: false },
    },
    trading: {
      enabled: true,
      mode: 'manual_live',
      riskProfileId: ids.conservative,
      riskProfileName: 'Conservative',
      capitalSource: 'fixed',
      capitalAmount: 100_000,
      styles: ['intraday', 'swing'],
      instruments: { intraday: ['EQ_MIS'], swingBuy: ['EQ_CNC'], swingSell: ['FUT'] },
      overrides: { riskPerTradePct: null, maxNewEntriesPerDay: null, maxOpenPositions: null, dailyLossPct: null },
    },
    portfolio: {
      enabled: false,
      defaultBucket: 'Test Dhan Swing · Portfolio',
      autoBucketName: 'Test Dhan Swing · Portfolio',
      includeInTotals: true,
      ...offRefresh,
      hasHoldings: false,
    },
    data: { enabled: true, primary: false, standby: true, liveFeedBudget: 200 },
    ...overrides,
  });
}

export function summaryOf(detail: AccountDetail): AccountSummary {
  return {
    id: detail.id,
    slug: detail.slug,
    name: detail.name,
    broker: detail.broker,
    maskedClientId: detail.maskedClientId,
    color: detail.color,
    roles: detail.roles,
    secrets: detail.secrets,
    apiKeyExpiresOn: detail.apiKeyExpiresOn,
    tokenStatus: detail.tokenStatus,
    connection: detail.connection,
  };
}

/** The account with another connection; tokenStatus follows, as in the API. */
export const withConnection = (detail: AccountDetail, connection: BrokerConnection): AccountDetail => ({
  ...detail,
  connection,
  tokenStatus: connection.status,
});

export const buildEvents = (): AccountEvent[] => [
  {
    at: '2026-10-10T08:30:00Z',
    event: 'TradingUpdated',
    detail: 'Trading settings changed: paper',
    actor: 'example@invalid.test',
  },
  {
    at: '2026-10-09T09:00:00Z',
    event: 'AccountCreated',
    detail: "Account 'Test Dhan Main' created",
    actor: 'example@invalid.test',
  },
];

// ---- Instruments (made-up rows in the shape of Dhan's master list) --------------------------------------------------

const instrumentId = (n: number) => `00000000-0000-4000-8000-0000000002${String(n).padStart(2, '0')}`;

/** A not-tracked instrument's tracking block. */
export const untrackedInfo: TrackedInfo = {
  active: false,
  since: null,
  deactivatedAt: null,
  dataFrom: null,
  dataTo: null,
  lastUpdate: null,
  backfillRequestedAt: null,
  retiredButTracked: false,
};

/** A tracked instrument's block (no history collected yet unless `overrides` says so). */
export const trackedInfo = (overrides: Partial<TrackedInfo> = {}): TrackedInfo => ({
  ...untrackedInfo,
  active: true,
  since: '2026-10-12T04:00:00Z',
  backfillRequestedAt: '2026-10-12T04:00:00Z',
  ...overrides,
});

export function buildInstrument(overrides: Partial<InstrumentSummary> = {}): InstrumentSummary {
  return {
    id: instrumentId(1),
    symbol: 'NSE:RELIANCE',
    name: 'RELIANCE INDUSTRIES LTD',
    displayName: 'Reliance Industries',
    type: 'share',
    isin: 'INE002A01018',
    series: 'EQ',
    sector: 'Oil Gas & Consumable Fuels',
    lotSize: 1,
    tickSize: 0.1,
    expiryDate: null,
    fnoEligible: true,
    asmGsm: false,
    retired: false,
    tracked: untrackedInfo,
    ...overrides,
  };
}

/** A small master list covering every type, flag and sector case. */
export const sampleInstruments: InstrumentSummary[] = [
  buildInstrument(),
  buildInstrument({
    id: instrumentId(2),
    symbol: 'NSE:TCS',
    name: 'TATA CONSULTANCY SERVICES LTD',
    displayName: 'Tata Consultancy Services',
    isin: 'INE467B01029',
    sector: 'Information Technology',
  }),
  buildInstrument({
    id: instrumentId(3),
    symbol: 'NSE:HDFCBANK',
    name: 'HDFC BANK LTD',
    displayName: 'HDFC Bank',
    isin: 'INE040A01034',
    sector: 'Financial Services',
  }),
  buildInstrument({
    id: instrumentId(4),
    symbol: 'NSE:YESBANK',
    name: 'YES BANK LTD',
    displayName: 'Yes Bank',
    isin: 'INE528G01035',
    sector: 'Financial Services',
    fnoEligible: false,
    asmGsm: true,
  }),
  buildInstrument({
    id: instrumentId(5),
    symbol: 'NSE:TINYCO',
    name: 'TINY CO LTD',
    displayName: 'Tiny Co',
    isin: 'INE000T01010',
    series: 'SM',
    sector: null,
    lotSize: 100,
    tickSize: 0.05,
    fnoEligible: false,
  }),
  buildInstrument({
    id: instrumentId(6),
    symbol: 'NSE:NIFTYBEES',
    name: 'NIPPON INDIA ETF NIFTY 50 BEES',
    displayName: 'Nippon Nifty BeES',
    type: 'etf',
    isin: 'INF204KB14I2',
    sector: null,
    fnoEligible: false,
  }),
  buildInstrument({
    id: instrumentId(7),
    symbol: 'NSE:NIFTY',
    name: 'NIFTY',
    displayName: 'Nifty 50',
    type: 'index',
    isin: null,
    series: null,
    sector: null,
    tickSize: null,
    fnoEligible: false,
  }),
  buildInstrument({
    id: instrumentId(8),
    symbol: 'NSE:RELIANCE-2026-10-FUT',
    name: 'RELIANCE-Oct2026-FUT',
    displayName: 'Reliance Oct 2026 Future',
    type: 'future',
    isin: null,
    series: null,
    sector: null,
    lotSize: 500,
    expiryDate: '2026-10-27',
    fnoEligible: false,
  }),
  buildInstrument({
    id: instrumentId(9),
    symbol: 'NSE:OLDCO',
    name: 'OLD CO LTD',
    displayName: 'Old Co',
    isin: 'INE000O01010',
    sector: null,
    fnoEligible: false,
    retired: true,
  }),
];

export function buildInstrumentDetail(overrides: Partial<InstrumentDetail> = {}): InstrumentDetail {
  return {
    id: instrumentId(1),
    symbol: 'NSE:RELIANCE',
    exchange: 'NSE',
    exchangeSegment: 'NSE_EQ',
    type: 'share',
    name: 'RELIANCE INDUSTRIES LTD',
    displayName: 'Reliance Industries',
    isin: 'INE002A01018',
    series: 'EQ',
    lotSize: 1,
    tickSize: 0.1,
    expiryDate: null,
    underlyingSymbol: 'RELIANCE',
    sector: 'Oil Gas & Consumable Fuels',
    sectorSource: 'nse',
    fnoEligible: true,
    asmGsm: false,
    asmGsmFlag: 'N',
    asmGsmCategory: null,
    mtfLeverage: 4.5,
    freezeQuantity: 81_195,
    firstSeenAt: '2026-10-11T03:15:00Z',
    lastSeenAt: '2026-10-12T03:15:00Z',
    retiredAt: null,
    brokerIds: [{ broker: 'dhan', brokerId: '2885', segment: 'NSE_EQ' }],
    tracked: untrackedInfo,
    ...overrides,
  };
}

const countBy = (items: InstrumentSummary[], key: (i: InstrumentSummary) => string | null) =>
  [
    ...items.reduce((map, item) => {
      const value = key(item) ?? 'Unclassified';
      return map.set(value, (map.get(value) ?? 0) + 1);
    }, new Map<string, number>()),
  ]
    .map(([value, count]) => ({ value, count }))
    .sort((a, b) =>
      a.value === 'Unclassified' ? 1 : b.value === 'Unclassified' ? -1 : a.value.localeCompare(b.value),
    );

/** Facets of a list, as the API computes them: active instruments only, "Unclassified" last. */
export function buildFacets(items: InstrumentSummary[] = sampleInstruments): InstrumentFacets {
  const active = items.filter((i) => !i.retired);
  return {
    total: active.length,
    retired: items.length - active.length,
    types: countBy(active, (i) => i.type),
    sectors: countBy(active, (i) => i.sector),
    series: countBy(
      active.filter((i) => i.series),
      (i) => i.series,
    ),
    fnoEligible: active.filter((i) => i.fnoEligible).length,
    asmGsm: active.filter((i) => i.asmGsm).length,
    tracked: items.filter((i) => i.tracked.active).length,
    trackedRetired: items.filter((i) => i.tracked.active && i.retired).length,
  };
}

export function buildInstrumentPage(
  items: InstrumentSummary[],
  overrides: Partial<InstrumentPage> = {},
): InstrumentPage {
  return { items, page: 1, pageSize: 50, total: items.length, ...overrides };
}

const sortKeys: Record<string, (i: InstrumentSummary) => string | number> = {
  symbol: (i) => i.symbol,
  name: (i) => i.displayName,
  type: (i) => i.type,
  series: (i) => i.series ?? '',
  sector: (i) => i.sector ?? '',
  lot: (i) => i.lotSize,
  tick: (i) => i.tickSize ?? 0,
};

/** The search the API performs, over an in-memory list: filters combine, `-` prefix sorts descending. */
export function searchInstruments(items: InstrumentSummary[], params: URLSearchParams): InstrumentPage {
  const q = params.get('q')?.toLowerCase();
  const sector = params.get('sector');
  const trackedParam = params.get('tracked');
  const tracked = trackedParam === null ? undefined : trackedParam === 'true';
  const sort = params.get('sort') ?? 'symbol';
  const descending = sort.startsWith('-');
  const key = sortKeys[descending ? sort.slice(1) : sort] ?? sortKeys.symbol!;
  const page = Number(params.get('page') ?? 1);
  const pageSize = Number(params.get('pageSize') ?? 50);
  const matches = items
    .filter((i) => params.get('includeRetired') === 'true' || !i.retired || (tracked === true && i.tracked.active))
    .filter((i) => tracked === undefined || i.tracked.active === tracked)
    .filter((i) => !q || [i.symbol, i.name, i.displayName, i.isin ?? ''].some((v) => v.toLowerCase().includes(q)))
    .filter((i) => !params.get('type') || i.type === params.get('type'))
    .filter((i) => !sector || (sector === 'Unclassified' ? i.sector === null : i.sector === sector))
    .filter((i) => !params.get('series') || i.series === params.get('series'))
    .filter((i) => params.get('fno') !== 'true' || i.fnoEligible)
    .filter((i) => params.get('asmGsm') !== 'true' || i.asmGsm)
    .sort((a, b) => {
      const [x, y] = [key(a), key(b)];
      const order = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y));
      return descending ? -order : order;
    });
  return { items: matches.slice((page - 1) * pageSize, page * pageSize), page, pageSize, total: matches.length };
}

export function buildRefreshRun(overrides: Partial<RefreshRun> = {}): RefreshRun {
  return {
    id: '00000000-0000-4000-8000-0000000003a1',
    trigger: 'manual',
    status: 'succeeded',
    phase: null,
    startedAt: '2026-10-12T03:15:00Z',
    finishedAt: '2026-10-12T03:15:40Z',
    requestedBy: 'example@invalid.test',
    rowsRead: 207_560,
    added: 12,
    updated: 3,
    retired: 5,
    revived: 0,
    unchanged: 4_361,
    skippedByScope: 203_160,
    collisions: 0,
    sectorMatched: 751,
    sectorUnmatched: 3_630,
    fnoUnderlyings: 213,
    totalActive: 4_381,
    keptByType: { share: 3_204, etf: 405, index: 119, future: 653 },
    warnings: [],
    error: null,
    ...overrides,
  };
}

/** A run that is in progress. */
export const buildRunningRun = (overrides: Partial<RefreshRun> = {}): RefreshRun =>
  buildRefreshRun({
    id: '00000000-0000-4000-8000-0000000003b2',
    status: 'running',
    phase: 'parsing',
    finishedAt: null,
    rowsRead: 120_000,
    added: 0,
    updated: 0,
    retired: 0,
    unchanged: 0,
    totalActive: 0,
    keptByType: {},
    ...overrides,
  });

export function buildRefreshStatus(overrides: Partial<RefreshStatus> = {}): RefreshStatus {
  return {
    current: null,
    last: buildRefreshRun(),
    autoRefresh: { enabled: true, timeIst: '08:45', nextRunAt: '2026-10-13T03:15:00Z' },
    totalActive: 4_381,
    ...overrides,
  };
}

// ---- Portfolio (made-up holdings of the made-up main account) -----------------------------------------------------

const holdingId = (n: number) => `00000000-0000-4000-8000-0000000004${String(n).padStart(2, '0')}`;
const lotId = (n: number) => `00000000-0000-4000-8000-0000000005${String(n).padStart(2, '0')}`;
const mainBucketId = '00000000-0000-4000-8000-0000000000b1';

export const lotIds = { first: lotId(1), second: lotId(2), closed: lotId(3) } as const;

export const holdingIds = {
  reliance: holdingId(1),
  tcs: holdingId(2),
  infy: holdingId(3),
  smallco: holdingId(4),
  noprice: holdingId(6),
  oldco: holdingId(5),
} as const;

/** A gain of 16.1 % with an XIRR of 14.2 % on ₹1,00,000 invested; override what the test is about. */
export function buildReturns(overrides: Partial<Returns> = {}): Returns {
  return {
    invested: 100_000,
    value: 116_100,
    pnl: 16_100,
    pnlPct: 16.1,
    xirr: 14.2,
    xirrState: 'ok',
    realisedPnl: 0,
    realisedEstimated: false,
    ...overrides,
  };
}

/** Figures of a holding that cannot be valued: no price, so no percentage and no XIRR. */
export const noValueReturns = (overrides: Partial<Returns> = {}): Returns =>
  buildReturns({ value: null, pnl: null, pnlPct: null, xirr: null, xirrState: 'no_price', ...overrides });

export function buildHolding(overrides: Partial<HoldingRow> = {}): HoldingRow {
  return {
    id: holdingIds.reliance,
    accountId: ids.main,
    accountName: 'Test Dhan Main',
    bucketId: mainBucketId,
    bucketName: 'Test Dhan Main · Portfolio',
    instrumentId: instrumentId(1),
    canonicalSymbol: 'NSE:RELIANCE',
    mapped: true,
    exchange: 'NSE',
    securityId: '2885',
    isin: 'INE002A01018',
    symbol: 'RELIANCE',
    name: 'Reliance Industries',
    qty: 40,
    avgCost: 2_500,
    lastPrice: 2_902.5,
    lastPriceAt: '2026-10-11T09:55:00Z',
    priced: true,
    status: 'open',
    firstSeenAt: '2026-10-01T05:00:00Z',
    closedAt: null,
    syncedAt: '2026-10-11T09:55:00Z',
    lotCount: 1,
    earliestAcquiredOn: '2024-03-05',
    usesDefaultDate: false,
    returns: buildReturns(),
    ...overrides,
  };
}

/** A gain, a loss, a holding on the default date, one not in the master list and one closed. */
export const sampleHoldings: HoldingRow[] = [
  buildHolding(),
  buildHolding({
    id: holdingIds.tcs,
    symbol: 'TCS',
    name: 'Tata Consultancy Services',
    isin: 'INE467B01029',
    securityId: '11536',
    qty: 10,
    avgCost: 4_000,
    lastPrice: 3_880,
    returns: buildReturns({ invested: 40_000, value: 38_800, pnl: -1_200, pnlPct: -3, xirr: -2.5 }),
  }),
  buildHolding({
    id: holdingIds.infy,
    symbol: 'INFY',
    name: 'Infosys',
    isin: 'INE009A01021',
    securityId: '1594',
    qty: 25,
    avgCost: 1_600,
    lastPrice: 1_700,
    lotCount: 2,
    earliestAcquiredOn: '2025-01-01',
    usesDefaultDate: true,
    returns: buildReturns({
      invested: 40_000,
      value: 42_500,
      pnl: 2_500,
      pnlPct: 6.25,
      xirr: null,
      xirrState: 'insufficient',
    }),
  }),
  buildHolding({
    id: holdingIds.smallco,
    symbol: 'SMALLCO',
    name: 'Small Co Ltd',
    instrumentId: null,
    canonicalSymbol: null,
    mapped: false,
    exchange: 'BSE',
    isin: null,
    securityId: '500999',
    qty: 100,
    avgCost: 50,
    lastPrice: 60,
    returns: buildReturns({ invested: 5_000, value: 6_000, pnl: 1_000, pnlPct: 20, xirr: 1_500, xirrState: 'n_m' }),
  }),
  buildHolding({
    id: holdingIds.oldco,
    symbol: 'OLDCO',
    name: 'Old Co Ltd',
    isin: 'INE000000001',
    securityId: '777',
    qty: 0,
    avgCost: 100,
    lastPrice: 120,
    status: 'closed',
    closedAt: '2026-09-30T05:00:00Z',
    lotCount: 0,
    earliestAcquiredOn: null,
    returns: buildReturns({
      invested: 0,
      value: 0,
      pnl: 0,
      pnlPct: null,
      xirr: null,
      xirrState: 'no_data',
      realisedPnl: 800,
      realisedEstimated: true,
    }),
  }),
];

const sortValue = (h: HoldingRow, field: string): number | string => {
  const numbers: Record<string, number> = {
    qty: h.qty,
    avgCost: h.avgCost,
    lastPrice: h.lastPrice,
    invested: h.returns.invested,
    value: h.returns.value ?? Number.NEGATIVE_INFINITY,
    pnl: h.returns.pnl ?? Number.NEGATIVE_INFINITY,
    pnlPct: h.returns.pnlPct ?? Number.NEGATIVE_INFINITY,
    xirr: h.returns.xirr ?? Number.NEGATIVE_INFINITY,
  };
  const texts: Record<string, string> = {
    symbol: h.symbol,
    name: h.name,
    acquiredOn: h.earliestAcquiredOn ?? '',
    bucket: h.bucketName,
    account: h.accountName,
  };
  return numbers[field] ?? texts[field] ?? h.symbol;
};

/** What GET /api/holdings answers over an in-memory list: status, account, bucket, search, sort and paging. */
export function searchHoldings(list: readonly HoldingRow[], params: URLSearchParams): HoldingPage {
  const status = params.get('status') ?? 'open';
  const q = (params.get('q') ?? '').toLowerCase();
  const sort = params.get('sort') ?? 'symbol';
  const field = sort.replace(/^-/, '');
  const direction = sort.startsWith('-') ? -1 : 1;
  const page = Number(params.get('page') ?? 1);
  const pageSize = Number(params.get('pageSize') ?? 50);
  const matches = list
    .filter((h) => status === 'all' || h.status === status)
    .filter((h) => !params.get('accountId') || h.accountId === params.get('accountId'))
    .filter((h) => !params.get('bucketId') || h.bucketId === params.get('bucketId'))
    .filter((h) => !q || `${h.symbol} ${h.name} ${h.isin ?? ''}`.toLowerCase().includes(q))
    .sort((a, b) => {
      const x = sortValue(a, field);
      const y = sortValue(b, field);
      return (x < y ? -1 : x > y ? 1 : 0) * direction;
    });
  return { items: matches.slice((page - 1) * pageSize, page * pageSize), page, pageSize, total: matches.length };
}

/** An open holding Dhan reported without a usable price: no value, no P&L, no XIRR. */
export const buildUnpricedHolding = (overrides: Partial<HoldingRow> = {}): HoldingRow =>
  buildHolding({
    id: holdingId(6),
    symbol: 'NOPRICE',
    name: 'No Price Ltd',
    isin: 'INE000000002',
    securityId: '888',
    qty: 10,
    avgCost: 200,
    lastPrice: 0,
    lastPriceAt: null,
    priced: false,
    returns: noValueReturns({ invested: 2_000 }),
    ...overrides,
  });

export function buildSyncRun(overrides: Partial<SyncRun> = {}): SyncRun {
  return {
    id: '00000000-0000-4000-8000-0000000006c1',
    accountId: ids.main,
    trigger: 'manual',
    status: 'succeeded',
    startedAt: '2026-10-11T09:54:50Z',
    finishedAt: '2026-10-11T09:55:00Z',
    newCount: 1,
    increasedCount: 0,
    decreasedCount: 0,
    closedCount: 1,
    unchangedCount: 3,
    error: null,
    ...overrides,
  };
}

/** Auto refresh off, one finished manual run. */
export function buildSyncStatus(overrides: Partial<SyncStatus> = {}): SyncStatus {
  return {
    mode: 'off',
    dailyTimeIst: '16:00',
    firstImportDate: '2025-01-01',
    hasHoldings: true,
    nextRunAt: null,
    running: false,
    last: buildSyncRun(),
    ...overrides,
  };
}

export function buildSummary(overrides: Partial<HoldingsSummary> = {}): HoldingsSummary {
  return {
    returns: buildReturns({
      invested: 185_000,
      value: 203_400,
      pnl: 18_400,
      pnlPct: 9.95,
      xirr: 11.3,
      realisedPnl: 800,
      realisedEstimated: true,
    }),
    openCount: 4,
    closedCount: 1,
    unmappedCount: 1,
    unpricedCount: 0,
    defaultDateLots: 2,
    valuationDate: '2026-10-11',
    lastSync: buildSyncRun(),
    ...overrides,
  };
}

export function buildLot(overrides: Partial<LotRow> = {}): LotRow {
  return {
    id: lotId(1),
    acquiredOn: '2024-03-05',
    qty: 40,
    unitCost: 2_500,
    source: 'import',
    usesDefaultDate: false,
    status: 'open',
    closedOn: null,
    closePrice: null,
    estimated: false,
    returns: buildReturns(),
    ...overrides,
  };
}

/** Two open lots (one on the default date) and one closed, estimated lot. */
export function buildLots(overrides: Partial<HoldingLots> = {}): HoldingLots {
  return {
    holdingId: holdingIds.infy,
    symbol: 'INFY',
    lastPrice: 1_700,
    valuationDate: '2026-10-11',
    lots: [
      buildLot({
        id: lotId(1),
        acquiredOn: '2025-01-01',
        qty: 15,
        unitCost: 1_500,
        usesDefaultDate: true,
        returns: buildReturns({ invested: 22_500, value: 25_500, pnl: 3_000, pnlPct: 13.33, xirr: 9.1 }),
      }),
      buildLot({
        id: lotId(2),
        acquiredOn: '2026-10-05',
        qty: 10,
        unitCost: 1_750,
        source: 'added',
        returns: buildReturns({
          invested: 17_500,
          value: 17_000,
          pnl: -500,
          pnlPct: -2.86,
          xirr: null,
          xirrState: 'insufficient',
        }),
      }),
      buildLot({
        id: lotId(3),
        acquiredOn: '2024-06-10',
        qty: 5,
        unitCost: 1_400,
        status: 'closed',
        closedOn: '2026-09-20',
        closePrice: 1_650,
        estimated: true,
        returns: buildReturns({ invested: 7_000, value: 8_250, pnl: 1_250, pnlPct: 17.86, xirr: 7.4 }),
      }),
    ],
    ...overrides,
  };
}

export const buildFirstImportView = (overrides: Partial<FirstImportView> = {}): FirstImportView => ({
  status: 'imported',
  imported: 3,
  defaultDated: 3,
  alreadyImported: 0,
  unknown: 0,
  errorCode: null,
  cause: null,
  message: null,
  ...overrides,
});

/** The answer of saving the portfolio settings: the account as saved and what the first import did (skipped: holdings exist, by default). */
export const buildPortfolioSaved = (
  account: AccountDetail,
  imported: FirstImportView = buildFirstImportView({ status: 'skipped', imported: 0, defaultDated: 0 }),
): PortfolioSaved => ({
  account,
  import: imported,
});

// ---- Baskets ---------------------------------------------------------------------------------------------------------

export const basketIds = {
  myChoice: '00000000-0000-4000-8000-000000000401',
  myLongTerm: '00000000-0000-4000-8000-000000000402',
  bestStocks: '00000000-0000-4000-8000-000000000403',
} as const;

export const buildBasket = (overrides: Partial<Basket> = {}): Basket => ({
  id: basketIds.myChoice,
  name: 'MyChoice',
  colour: '#6366f1',
  purpose: 'trading',
  description: null,
  position: 0,
  memberCount: 0,
  createdAt: '2026-10-11T04:00:00Z',
  archivedAt: null,
  ...overrides,
});

/** The three baskets the migration creates, empty. */
export const seededBaskets = (): Basket[] => [
  buildBasket(),
  buildBasket({ id: basketIds.myLongTerm, name: 'MyLongTerm', colour: '#10b981', purpose: 'long_term', position: 1 }),
  buildBasket({ id: basketIds.bestStocks, name: 'Best stocks', colour: '#f59e0b', position: 2 }),
];

export const buildBasketMember = (
  instrument: InstrumentSummary,
  overrides: Partial<BasketMember> = {},
): BasketMember => ({
  id: `00000000-0000-4000-8000-0000000005${instrument.id.slice(-2)}`,
  instrument,
  position: 0,
  validFrom: '2026-10-11T04:00:00Z',
  validTo: null,
  note: null,
  addedBy: 'example@invalid.test',
  ...overrides,
});

export const buildBasketMembers = (
  basket: Basket,
  items: BasketMember[],
  overrides: Partial<BasketMembers> = {},
): BasketMembers => ({
  basketId: basket.id,
  name: basket.name,
  archivedAt: null,
  asOf: null,
  total: items.length,
  items,
  ...overrides,
});

export const buildHistoryEntry = (overrides: Partial<BasketHistoryEntry> = {}): BasketHistoryEntry => ({
  memberId: '00000000-0000-4000-8000-000000000501',
  kind: 'added',
  at: '2026-10-11T04:00:00Z',
  instrumentId: instrumentId(2),
  symbol: 'NSE:TCS',
  name: 'TATA CONSULTANCY SERVICES LTD',
  type: 'share',
  note: null,
  by: 'example@invalid.test',
  ...overrides,
});

export const buildInstrumentBasket = (overrides: Partial<InstrumentBasket> = {}): InstrumentBasket => ({
  basketId: basketIds.myChoice,
  name: 'MyChoice',
  colour: '#6366f1',
  purpose: 'trading',
  position: 0,
  since: '2026-10-11T04:00:00Z',
  ...overrides,
});
