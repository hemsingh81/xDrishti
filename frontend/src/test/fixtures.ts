import type {
  AccountDetail,
  AccountEvent,
  AccountSummary,
  BrokerConnection,
  InstrumentDetail,
  InstrumentFacets,
  InstrumentPage,
  InstrumentSummary,
  PortfolioBucket,
  Problem,
  RefreshRun,
  RefreshStatus,
  RiskProfile,
  SessionInfo,
  SystemStatus,
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

export const buildBuckets = (): PortfolioBucket[] => [
  { id: '00000000-0000-4000-8000-0000000000b1', name: 'Trading' },
  { id: '00000000-0000-4000-8000-0000000000b2', name: 'MyLongTerm' },
  { id: '00000000-0000-4000-8000-0000000000b3', name: 'MyChoice' },
];

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
    portfolio: { enabled: true, defaultBucket: 'Trading', historyFrom: '2024-04-01', includeInTotals: true },
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
    portfolio: { enabled: false, defaultBucket: null, historyFrom: null, includeInTotals: true },
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
  const sort = params.get('sort') ?? 'symbol';
  const descending = sort.startsWith('-');
  const key = sortKeys[descending ? sort.slice(1) : sort] ?? sortKeys.symbol!;
  const page = Number(params.get('page') ?? 1);
  const pageSize = Number(params.get('pageSize') ?? 50);
  const matches = items
    .filter((i) => params.get('includeRetired') === 'true' || !i.retired)
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
