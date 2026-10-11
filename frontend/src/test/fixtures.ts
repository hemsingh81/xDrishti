import type {
  AccountDetail,
  AccountEvent,
  AccountSummary,
  BrokerConnection,
  PortfolioBucket,
  Problem,
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
