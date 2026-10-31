import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';

export const execAccountId = '11111111-1111-4111-8111-111111111111';

export const buildExecStatus = (
  over: Partial<Schemas['ExecutionStatusView']> = {},
): Schemas['ExecutionStatusView'] => ({
  service: { state: 'down', lastBeatAt: null },
  environment: 'none',
  accounts: [],
  halts: [],
  openIntents: 0,
  settingsHash: null,
  entriesBlocked: true,
  anyAutomated: false,
  killSwitchActive: false,
  pollSeconds: 60,
  marketOpen: false,
  ...over,
});

const value = (v: number, cap: number | null = null): Schemas['ExecMetric'] => ({ state: 'value', value: v, cap });

export const buildOverview = (over: Partial<Schemas['ExecutionOverview']> = {}): Schemas['ExecutionOverview'] => ({
  date: '2026-10-30',
  canaryPending: false,
  executionEnabled: false,
  accounts: [],
  limits: ['Marks are at the last price; costs are not included.'],
  ...over,
});

export const buildOverviewAccount = (over: Partial<Schemas['OverviewAccount']> = {}): Schemas['OverviewAccount'] => ({
  accountId: execAccountId,
  name: 'Test Dhan Main',
  mode: 'assisted',
  effectiveFrom: '2026-10-29T03:45:00Z',
  pendingMode: null,
  pendingFrom: null,
  gate: { open: true, reason: 'open' },
  readiness: { state: 'ready', checkedAt: null, coveredUntil: null },
  caps: {
    orders: value(3, 6),
    notional: value(148446, 150000),
    worstCaseLoss: value(1470, 1500),
    openPositions: value(1, 3),
    limitsId: 4,
    pendingLimits: null,
  },
  openTickets: 0,
  halts: [],
  alarms: [],
  positionsAtRisk: 0,
  ...over,
});

export const buildBlotterRow = (over: Partial<Schemas['BlotterRow']> = {}): Schemas['BlotterRow'] => ({
  intentId: '33333333-3333-4333-8333-333333333333',
  at: '2026-10-30T04:00:00Z',
  accountId: execAccountId,
  account: 'Test Dhan Main',
  symbol: 'TATAPOWER',
  side: 'BUY',
  quantity: 120,
  orderType: 'LIMIT',
  price: 412.5,
  state: 'filled',
  stateGroup: 'live',
  stateKnown: true,
  by: 'tap',
  canary: false,
  clientOrderId: 'xd-abcdef123456789',
  brokerOrderId: '2026103000001',
  algoId: null,
  filledQuantity: 120,
  fillPrice: 412.6,
  slippageTicks: { state: 'value', value: 2 },
  slippagePercent: { state: 'value', value: 0.02 },
  fills: [],
  rejectionCodes: [],
  brokerCode: null,
  ...over,
});

export const buildPosition = (over: Partial<Schemas['PositionRow']> = {}): Schemas['PositionRow'] => ({
  accountId: execAccountId,
  account: 'Test Dhan Main',
  symbol: 'TATAPOWER',
  netQuantity: 120,
  averagePrice: 412.6,
  lastPrice: { state: 'value', value: 415 },
  unrealised: { state: 'value', value: 288 },
  riskToStop: { state: 'value', value: 600 },
  stopPrice: 407.6,
  protection: 'confirmed',
  source: 'ours',
  reconciliation: 'matched',
  ...over,
});

export const buildHalt = (over: Partial<Schemas['ExecutionHaltView']> = {}): Schemas['ExecutionHaltView'] => ({
  id: 17,
  at: '2026-10-30T04:10:00Z',
  scope: 'system',
  accountId: null,
  level: 'entries',
  trigger: 'owner',
  source: 'app',
  actor: 'owner',
  reason: 'Stopped from the app bar (entries)',
  end: null,
  ...over,
});

/** Defaults for every execution read: execution off, nothing to show. Tests override what they care about. */
export const executionHandlers = [
  http.get('/api/execution/status', () => HttpResponse.json(buildExecStatus())),
  http.get('/api/execution/overview', () => HttpResponse.json(buildOverview())),
  http.get('/api/execution/positions', () => HttpResponse.json([])),
  http.get('/api/execution/notices', () => HttpResponse.json({ items: [] })),
];

export const buildModeAccount = (
  over: Partial<Schemas['ExecutionModeAccountView']> = {},
): Schemas['ExecutionModeAccountView'] => ({
  accountId: execAccountId,
  name: 'Test Dhan Main',
  configuredMode: 'manual_live',
  effectiveMode: 'manual_live',
  pending: null,
  request: null,
  next: {
    from: 'manual_live',
    to: 'assisted',
    verdict: 'eligible',
    checks: [
      { code: 'sessions', outcome: 'pass', text: 'Enough sessions in manual_live', n: 12, needed: 10 },
      { code: 'drill', outcome: 'unavailable', text: 'A kill-switch drill in the last 30 days', n: null, needed: null },
    ],
  },
  trail: [],
  ...over,
});

export const buildModes = (
  accounts: Schemas['ExecutionModeAccountView'][] = [buildModeAccount()],
): Schemas['ExecutionModesView'] => ({
  executionEnabled: false,
  gatePolicyVersion: 1,
  accounts,
});

export const buildLimitsView = (over: Partial<Schemas['LimitsView']> = {}): Schemas['LimitsView'] => ({
  inForce: null,
  pending: null,
  complete: false,
  missing: ['maxOrdersPerDay'],
  history: [],
  ceilings: {
    maxOrdersPerDay: 20,
    maxOpenPositions: 5,
    maxAllowList: 10,
    windowEarliest: '09:20',
    windowLatest: '15:00',
    products: ['CNC', 'INTRADAY'],
  },
  serverNow: '2026-10-30T04:00:00Z',
  ...over,
});

export const buildLimitsRow = (over: Partial<Schemas['LimitsRowView']> = {}): Schemas['LimitsRowView'] => ({
  id: 4,
  accountId: execAccountId,
  maxOrdersPerDay: 6,
  maxNotionalPerOrder: 50000,
  maxNotionalPerDay: 150000,
  maxLossPerDay: 1500,
  maxOpenPositions: 3,
  allowList: ['TATAPOWER'],
  windowStart: '09:30',
  windowEnd: '14:30',
  products: ['CNC'],
  exitAllOnLossHalt: true,
  actor: 'owner',
  reason: 'start',
  changedAt: '2026-10-20T04:00:00Z',
  effectiveFrom: '2026-10-21T03:30:00Z',
  snapshotHash: 'h',
  ...over,
});
