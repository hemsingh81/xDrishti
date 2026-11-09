import type { Schemas } from '@/shared/api';

/** A recorder switch view that is OFF and never changed (the option chain recorder; the backfill adds its progress). */
export const buildOptionRecorderView = (over: Partial<Schemas['OptionChainRecorderView']> = {}) => ({
  enabled: false,
  switchId: null,
  state: null,
  lastSeenAt: null,
  notRunningBecause: ['Your switch is OFF.'],
  notice: 'Off until the deployment flag, your switch (with your password) and the service are all on.',
  history: [],
  ...over,
});

export const buildBackfillView = (
  over: Partial<Schemas['OptionBackfillView']> = {},
): Schemas['OptionBackfillView'] => ({
  ...buildOptionRecorderView(),
  probeAccepted: null,
  progress: null,
  ...over,
});

export const buildCoverageView = (
  over: Partial<Schemas['OptionCoverageView']> = {},
): Schemas['OptionCoverageView'] => ({
  underlying: 'NSE:NIFTY',
  sessions: [],
  usableChainSessions: 0,
  truncated: false,
  ...over,
});

const ok = (value: number) => ({ state: 'ok', value, reason: null });
const none = (reason: string) => ({ state: 'unavailable', value: null, reason });

export const buildUnderlyingSummary = (
  over: Partial<Schemas['UnderlyingArchiveSummary']> = {},
): Schemas['UnderlyingArchiveSummary'] => ({
  underlying: 'NSE:NIFTY',
  recordedSessions: ok(40),
  usableSessions: ok(31),
  firstDate: { state: 'ok', value: '2026-09-01', reason: null },
  lastDate: { state: 'ok', value: '2026-10-30', reason: null },
  gaps: { state: 'ok', reason: null, outcomes: [{ outcome: 'not_usable', reason: 'coverage', sessions: 9 }] },
  backfill: { state: 'ok', reason: null, done: 120, empty: 3, noData: 2, givenUp: 1 },
  spreadModelSessions: none('no_model'),
  spreadModelFirstSession: { state: 'unavailable', value: null, reason: 'no_model' },
  spreadModelLastSession: { state: 'unavailable', value: null, reason: 'no_model' },
  ...over,
});

export const buildExpressionRun = (
  over: Partial<Schemas['ExpressionRunSummary']> = {},
): Schemas['ExpressionRunSummary'] => ({
  id: 'r1',
  cashRunKey: 'cash-1',
  profileHash: 'abcdef0123456789'.repeat(4),
  remarkVersion: 'rm-1',
  createdAt: '2026-10-30T10:00:00+05:30',
  trades: 200,
  paired: 120,
  unpaired: [{ reason: 'no_chain', trades: 80 }],
  fallbackShare: ok(0.25),
  expiryDerivedShare: none('not_recorded'),
  spreadFromRecentArchiveShare: ok(0.1),
  lotFeasibility: [],
  labels: [],
  ...over,
});

export const buildPairFormation = (
  over: Partial<Schemas['PairFormationSummary']> = {},
): Schemas['PairFormationSummary'] => ({
  id: 'p1',
  family: 'pairs:intraday-1',
  quarter: '2026Q3',
  windowFrom: '2026-07-01',
  windowTo: '2026-09-30',
  createdAt: '2026-10-01T10:00:00+05:30',
  candidates: 300,
  tested: 280,
  unavailable: 20,
  selected: 0,
  bhThreshold: none('cut'),
  halfLives: { state: 'unavailable', reason: 'none_selected', count: null, min: null, median: null, max: null },
  ...over,
});
