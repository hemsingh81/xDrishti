import type { Schemas } from '@/shared/api';

export const RUN = '11111111-1111-1111-1111-111111111111';
export const BASE = '44444444-4444-4444-4444-444444444444';
export const PAIR = '33333333-3333-3333-3333-333333333333';
export const SWEEP = '55555555-5555-5555-5555-555555555555';
export const COSTS = 'before costs (charge sheet not filled, ISS-25)';

const completeness = { complete: true, sessionsDone: 10, sessionsTotal: 10, asOfSession: '2024-01-12' };

export const runItem = (over: Partial<Schemas['ReplayRunListItem']> = {}): Schemas['ReplayRunListItem'] => ({
  id: RUN,
  pairId: PAIR,
  createdAt: '2026-11-12T03:00:00Z',
  from: '2024-01-01',
  to: '2024-01-12',
  mode: 'frozen',
  state: 'completed',
  completeness,
  finalEquity: 1_050_000,
  maxDrawdownPct: 2,
  trialNumber: 7,
  costsLabel: COSTS,
  arm: 'selection',
  hindsight: [],
  ...over,
});

export const armComparison = (
  over: Partial<Schemas['ReplayArmComparisonView']> = {},
): Schemas['ReplayArmComparisonView'] => ({
  selection: runItem(),
  baseline: runItem({ id: BASE, arm: 'baseline' }),
  answer: 'selection_better',
  reason: null,
  days: 250,
  informativeDays: 140,
  meanDailyDifference: 0.0004,
  probabilitySelectionBetter: 0.97,
  probabilityBaselineBetter: 0.03,
  low: 0.0001,
  high: 0.0007,
  cut: 0.95,
  hurtsBar: 0.95,
  minDays: 20,
  label: 'baseline_no_selection',
  shortcut: 'Equity is realised-only: open positions are not marked to market.',
  ...over,
});

export const baselinePair = (over: Partial<Schemas['ReplayPairView']> = {}): Schemas['ReplayPairView'] => ({
  hindsight: [{ code: 'settings_now', text: 'Settings are as of today.' }],
  pairId: PAIR,
  frozen: null,
  learning: null,
  answer: null,
  reason: null,
  days: null,
  informativeDays: null,
  meanDailyDifference: null,
  probabilityLearningBetter: null,
  probabilityFrozenBetter: null,
  low: null,
  high: null,
  cut: null,
  hurtsBar: null,
  minDays: null,
  selectionVsBaseline: armComparison(),
  ...over,
});

export const sweepRow = (over: Partial<Schemas['ReplaySweepRunView']> = {}): Schemas['ReplaySweepRunView'] => ({
  runId: RUN,
  from: '2025-01-01',
  state: 'completed',
  sessionsDone: 100,
  sessionsTotal: 100,
  finalEquity: 1_100_000,
  cagr: 0.1,
  maxDrawdownPct: 8,
  trades: 42,
  beatsBenchmark: true,
  failReason: null,
  ...over,
});

export const sweepView = (over: Partial<Schemas['ReplaySweepView']> = {}): Schemas['ReplaySweepView'] => ({
  hindsight: [{ code: 'settings_now', text: 'Settings are as of today.' }],
  sweepId: SWEEP,
  to: '2025-06-30',
  createdAt: '2026-11-12T03:00:00Z',
  state: 'completed',
  complete: true,
  looksAdded: 2,
  completedStarts: 2,
  costsLabel: COSTS,
  starts: [
    sweepRow(),
    sweepRow({ runId: BASE, from: '2025-04-01', cagr: -0.041, maxDrawdownPct: 12, beatsBenchmark: false }),
  ],
  spread: {
    starts: 2,
    cagr: { min: -0.041, median: 0.0295, max: 0.1 },
    maxDrawdownPct: { min: 8, median: 10, max: 12 },
    shareCagrPositive: 0.5,
    shareBeatingBenchmark: 0.5,
  },
  ...over,
});

export const tradeChart = (over: Partial<Schemas['ReplayTradeChartView']> = {}): Schemas['ReplayTradeChartView'] => ({
  hindsight: [{ code: 'settings_now', text: 'Settings are as of today.' }],
  runId: RUN,
  tradeNo: 1,
  symbol: 'AAA',
  side: 'BUY',
  qty: 10,
  entrySession: '2024-01-03',
  entryAt: '2024-01-03T04:30:00Z',
  entryPrice: 100,
  stopPrice: 98,
  exitSession: '2024-01-03',
  exitAt: '2024-01-03T04:35:00Z',
  exitPrice: 105,
  exitPriceDerived: true,
  grossPnl: 50,
  netPnl: null,
  costsLabel: COSTS,
  chart: {
    timeframe: '5m',
    adjusted: false,
    adjustedThrough: null,
    from: '2024-01-03T03:45:00Z',
    to: '2024-01-04T10:00:00Z',
    barCount: 2,
    bars: [
      { t: 1_704_256_200, o: 100, h: 101, l: 99, c: 100.5, v: 1000 },
      { t: 1_704_256_500, o: 100.5, h: 102, l: 100, c: 101, v: 900 },
    ],
    coherence: { state: 'ok', entryStored: 100, entryOnBars: 100, detail: 'The bars agree with the stored entry.' },
  },
  markers: [
    { kind: 'entry', at: '2024-01-03T04:30:00Z', price: 100 },
    { kind: 'stop', at: null, price: 98 },
    { kind: 'exit', at: '2024-01-03T04:35:00Z', price: 105 },
  ],
  limits: ['The bars stop at the hold-out clamp (2025-06-29).', 'Bars are not adjusted for later corporate actions.'],
  ...over,
});
