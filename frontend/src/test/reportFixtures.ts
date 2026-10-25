import type { Schemas } from '@/shared/api';

type Metric = Schemas['MetricView'];
type Group = Schemas['GroupView'];
type Evidence = Schemas['ReportEvidenceView'];
type Run = Schemas['RunListItemView'];
type WhatWorks = Schemas['WhatWorksView'];

export const runKeyA = 'a'.repeat(64);
export const runKeyB = 'b'.repeat(64);
export const limitsText =
  'In-sample on replayed history with modelled fills: a description of the past, not a forecast.';

/** A figure in a state; only a value or provisional carries a number. */
export const value = (number: number): Metric => ({ state: 'value', number, reason: null });
export const nonValue = (state: Metric['state'], reason = 'No reason given.'): Metric => ({
  state,
  number: null,
  reason,
});

export const buildRun = (over: Partial<Run> = {}): Run => ({
  runKey: runKeyA,
  strategyId: 'orb',
  universeKind: 'basket',
  universeLabel: 'MyChoice',
  windowStart: '2024-04-01',
  windowEnd: '2026-04-30',
  costed: false,
  costsSheetId: null,
  style: 'intraday',
  styleSource: 'strategy',
  exit: 'fixed_r',
  exitSource: 'comparison',
  closedTrades: 412,
  openAtEnd: 3,
  netPossible: false,
  inDefaultSet: true,
  defaultSetNote: null,
  createdAt: '2026-10-20T10:00:00Z',
  ...over,
});

export const buildRunsView = (
  runs: Run[] = [buildRun()],
  over: Partial<Schemas['RunsView']> = {},
): Schemas['RunsView'] => ({
  runs,
  net: {
    state: 'unavailable',
    reason: 'The charge sheet is not confirmed, so every report is gross.',
    confirmed: null,
  },
  defaultSetRule: 'The default run set is the newest run of each strategy and universe.',
  ...over,
});

export const buildEvidence = (over: Partial<Evidence> = {}): Evidence => ({
  basis: 'gross',
  n: 412,
  firstDate: '2024-04-02',
  lastDate: '2026-04-30',
  provisional: false,
  runKeys: [runKeyA],
  runs: [
    {
      runKey: runKeyA,
      strategyId: 'orb',
      universe: 'MyChoice',
      windowStart: '2024-04-01',
      windowEnd: '2026-04-30',
      costed: false,
      costsSheetId: null,
      style: 'intraday',
      exit: 'fixed_r',
      closedTrades: 412,
      openAtEnd: 3,
    },
  ],
  defaultRunSet: true,
  limits: [limitsText],
  unavailableInputs: [],
  duplicateTradesDropped: 0,
  openAtEndLeftOut: 3,
  netBasisConfirmation: null,
  ...over,
});

/** A group with enough trades; every figure a value unless overridden. */
export const buildGroup = (over: Partial<Group> = {}): Group => ({
  key: 'all',
  parts: [],
  n: 412,
  wins: 230,
  winRate: value(0.558),
  avgWinR: value(1.1),
  avgLossR: value(-0.9),
  expectancyR: value(0.21),
  profitFactor: value(1.4),
  totalR: value(86.5),
  maxDrawdownR: value(-12.3),
  longestLossStreak: value(7),
  avgHoldingSessions: value(2.4),
  shrunkWinRate: value(0.55),
  winRateLower: value(0.5),
  shrunkExpectancyR: value(0.18),
  expectancyLowerR: value(0.05),
  evidence: { n: 412, firstDate: '2024-04-02', lastDate: '2026-04-30', provisional: false, runKeys: [runKeyA] },
  ...over,
});

/** A group whose every figure is `figure` (a non-value, say). */
export const allFigures = (figure: Metric, over: Partial<Group> = {}): Group =>
  buildGroup({
    winRate: figure,
    avgWinR: figure,
    avgLossR: figure,
    expectancyR: figure,
    profitFactor: figure,
    totalR: figure,
    maxDrawdownR: figure,
    longestLossStreak: figure,
    avgHoldingSessions: figure,
    shrunkWinRate: figure,
    winRateLower: figure,
    shrunkExpectancyR: figure,
    expectancyLowerR: figure,
    ...over,
  });

export const partsOf = (dimension: string, v: string | null) => [{ dimension, value: v }];

export const buildWhatWorks = (over: Partial<WhatWorks> = {}): WhatWorks => ({
  kind: 'nothing_qualifies',
  dimensions: ['basket', 'strategy', 'timeframe', 'side', 'exit'],
  ranked: [],
  notRanked: [],
  cellsLookedAt: 24,
  cellsComparable: 12,
  minTrades: 30,
  requiredTrades: 45,
  largestN: 28,
  positiveLowerBound: 0,
  expectedChancePositives: 0.6,
  headline: 'Nothing has enough evidence yet.',
  selectionNote: 'The best of 24 looks good by chance.',
  unavailableDimensions: [],
  nonAdditive: true,
  evidence: buildEvidence(),
  ...over,
});

export const rankedWhatWorks = (): WhatWorks =>
  buildWhatWorks({
    kind: 'ranked',
    headline: 'MyChoice, orb, 15m, BUY looks best, with a lower bound of +0.05 R.',
    ranked: [1, 2, 3].map((rank) => ({
      rank,
      lowerBoundPositive: rank === 1,
      group: buildGroup({
        key: `cell-${String(rank)}`,
        n: 120 - rank,
        parts: [
          { dimension: 'basket', value: '00000000-0000-4000-8000-000000000401' },
          { dimension: 'strategy', value: 'orb' },
          { dimension: 'timeframe', value: '15m' },
        ],
      }),
    })),
    notRanked: [4, 5, 6, 7].map((i) => ({
      reason: `Only ${String(10 + i)} trades; 45 are needed.`,
      group: allFigures(nonValue('insufficient', 'Too few trades.'), {
        key: `thin-${String(i)}`,
        n: 10 + i,
        parts: [{ dimension: 'strategy', value: `thin${String(i)}` }],
      }),
    })),
    cellsLookedAt: 7,
    cellsComparable: 7,
    positiveLowerBound: 1,
    expectedChancePositives: 0.4,
    unavailableDimensions: ['exit is left out: no run recorded it'],
    largestN: 119,
  });

export const buildInsights = (): Schemas['InsightsView'] => ({
  insights: [
    ['best_strategy', 'value', 'Best strategy: orb at +0.21 R over 412 trades.'],
    ['weakest_strategy', 'insufficient', 'Weakest strategy: not stated, a group has fewer than 15 trades.'],
    ['basket_gap', 'value', 'Basket gap: MyChoice leads by 0.1 R.'],
    ['side_bias', 'value', 'Side bias: BUY leads.'],
    ['best_exit', 'unavailable', 'Best exit: no run recorded an exit.'],
    ['weekday', 'value', 'Weekday: Tue is best.'],
    ['regime', 'unavailable', 'Regime: no regime is stored.'],
    ['calibration', 'not_evaluable', 'Calibration: needs a champion model, and none is stored.'],
  ].map(([kind, state, text]) => ({ kind: kind ?? '', state: state ?? '', text: text ?? '', cells: [] })),
  evidence: buildEvidence(),
});

export const buildEquity = (): Schemas['EquityView'] => ({
  dimensions: [],
  nonAdditive: false,
  series: [
    {
      key: 'all',
      parts: [],
      n: 412,
      totalR: value(86.5),
      points: [
        { date: '2026-03-02', cumulativeR: 1.5, drawdownR: 0, trades: 2 },
        { date: '2026-03-03', cumulativeR: 0.5, drawdownR: 1, trades: 1 },
        { date: '2026-03-04', cumulativeR: 3, drawdownR: 0, trades: 3 },
      ],
      evidence: { n: 412, firstDate: '2024-04-02', lastDate: '2026-04-30', provisional: false, runKeys: [runKeyA] },
    },
  ],
  evidence: buildEvidence(),
});

export const buildDistribution = (): Schemas['DistributionView'] => ({
  binWidthR: 0.5,
  dimensions: [],
  nonAdditive: false,
  groups: [
    {
      key: 'all',
      parts: [],
      n: 412,
      meanR: value(0.21),
      bins: [
        { lowerR: null, upperR: -4, count: 2 },
        { lowerR: -4, upperR: -3.5, count: 5 },
        { lowerR: -0.5, upperR: 0, count: 90 },
        { lowerR: 6, upperR: null, count: 4 },
      ],
      evidence: { n: 412, firstDate: '2024-04-02', lastDate: '2026-04-30', provisional: false, runKeys: [runKeyA] },
    },
  ],
  evidence: buildEvidence(),
});

export const buildGroups = (
  dimension: string,
  groups: Group[],
  over: Partial<Schemas['GroupsView']> = {},
): Schemas['GroupsView'] => ({
  dimensions: [dimension],
  nonAdditive: dimension === 'basket',
  groups,
  evidence: buildEvidence(),
  ...over,
});

export const buildPivot = (): Schemas['PivotView'] => ({
  rowDimension: 'strategy',
  columnDimension: 'timeframe',
  metric: 'expectancy_r',
  rows: ['orb'],
  columns: ['15m', '1h'],
  cells: [
    { row: 'orb', column: '15m', n: 200, value: value(0.3) },
    { row: 'orb', column: '1h', n: 8, value: nonValue('insufficient', 'Too few trades.') },
  ],
  nonAdditive: false,
  evidence: buildEvidence(),
});

export const buildMaeMfe = (over: Partial<Schemas['MaeMfeGroupView']> = {}): Schemas['ReportMaeMfeView'] => ({
  dimensions: [],
  nonAdditive: false,
  groups: [
    {
      key: 'all',
      parts: [],
      n: 40,
      excursionN: 40,
      winnersMaeP90: value(0.75),
      grid: {
        maeEdges: [0, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2],
        resultEdges: [-5, -4.5, -4, -3.5, -3, -2.5, -2, -1.5, -1, -0.5, 0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5, 5],
        counts: Array.from({ length: 9 }, (_, row) =>
          Array.from({ length: 22 }, (_, col) => (row === 1 && col === 13 ? 40 : 0)),
        ),
        belowCount: 0,
        aboveCount: 0,
      },
      mfe: {
        edges: [0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5, 5],
        counts: [2, 5, 10, 8, 6, 4, 2, 1, 1, 0],
        aboveCount: 1,
      },
      reachedMfe: [
        { atR: 1, count: 32, share: 0.8 },
        { atR: 2, count: 14, share: 0.35 },
        { atR: 3, count: 5, share: 0.125 },
      ],
      evidence: { n: 40, firstDate: '2024-04-02', lastDate: '2026-04-30', provisional: false, runKeys: [runKeyA] },
      ...over,
    },
  ],
  evidence: buildEvidence(),
});
