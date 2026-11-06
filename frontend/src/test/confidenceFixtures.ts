import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { problemResponse, server } from './server';

type DecayReport = Schemas['DecayReportView'];
type DecaySeries = Schemas['DecaySeriesView'];
type DecayKill = Schemas['DecayKillView'];
type LoopReport = Schemas['LoopReportView'];
type Proposals = Schemas['KillProposalsView'];
type Proposal = Schemas['KillProposalView'];

/** A series with two completed windows, the second below its level, and two quarters. */
export const buildSeries = (over: Partial<DecaySeries> = {}): DecaySeries => ({
  basis: 'net R',
  note: null,
  state: 'alert',
  trades: 130,
  pendingTrades: 10,
  streak: 1,
  twiceInARow: false,
  firstTwiceAtWindow: null,
  windows: [
    {
      index: 1,
      trades: 60,
      firstDay: '2026-01-05',
      lastDay: '2026-03-20',
      meanR: 0.12,
      designEffect: 1.6,
      threshold: -0.09,
      below: false,
    },
    {
      index: 2,
      trades: 60,
      firstDay: '2026-03-23',
      lastDay: '2026-06-12',
      meanR: -0.14,
      designEffect: 1.6,
      threshold: -0.09,
      below: true,
    },
  ],
  cusum: { alarm: true, events: 1, latest: 'CUSUM alarm on 2026-06-10.' },
  quarters: [
    { label: '2026-Q1', trades: 60, meanR: 0.12, weak: false },
    { label: '2026-Q2', trades: 70, meanR: -0.11, weak: true },
  ],
  weakQuarterShare: 0.5,
  ...over,
});

/** A series that cannot be judged: no reference, nothing computed. */
export const emptySeries = (): DecaySeries =>
  buildSeries({
    state: 'unavailable',
    trades: 0,
    pendingTrades: 0,
    streak: 0,
    windows: [],
    cusum: null,
    quarters: [],
    weakQuarterShare: null,
    note: 'No reference, so the series is not judged.',
  });

export const buildKill = (over: Partial<DecayKill> = {}): DecayKill => ({
  rule: 'decay_twice',
  state: 'holds',
  value: 1,
  limit: 2,
  trades: 130,
  proposalOnly: false,
  text: 'One window below its level; the rule needs two in a row.',
  ...over,
});

/** The four rules, one in each state. */
export const fourKills = (): DecayKill[] => [
  buildKill({ rule: 'cost_stress', state: 'fires', value: -0.04, limit: 0, text: 'Net R at 2x costs is -0.04.' }),
  buildKill({ rule: 'drawdown', state: 'not_evaluated', value: null, limit: null, text: 'No cell on a shadow row.' }),
  buildKill({ rule: 'decay_twice', state: 'holds' }),
  buildKill({
    rule: 'failure_boundary',
    state: 'held',
    value: null,
    limit: null,
    proposalOnly: true,
    text: 'The wait since the last firing has not passed.',
  }),
];

export const buildDecayReport = (over: Partial<DecayReport> = {}): DecayReport => ({
  strategy: 'orb',
  epoch: 3,
  hasSeries: true,
  settings: {
    windowTrades: 60,
    sigmas: 1.5,
    quarterFloorR: -0.05,
    settingsHash: 'dec-hash-1',
    settings: 'window=60;sigmas=1.5',
    rho: 0.3,
    rhoIsPlaceholder: true,
  },
  reference: { state: 'available', reason: null, trades: 400, meanR: 0.08, sigma: 1.1, basis: 'net' },
  edge: buildSeries(),
  behavioural: emptySeries(),
  kill: fourKills(),
  statements: ['A flag is not a pass.'],
  line: 'orb: alert on the latest window.',
  ...over,
});

/** Today's reality: no stored scores, no refit loop. */
export const buildLoopReport = (over: Partial<LoopReport> = {}): LoopReport => ({
  settings: {
    minDays: 20,
    draws: 2000,
    level: 0.9,
    helpsAt: 0.95,
    minSlopePer50Rows: 0.002,
    targetGain: 0.01,
    slopeWindow: 200,
    slopeLow: 0.8,
    slopeHigh: 1.2,
    slopeSe: 2,
    settingsHash: 'loop-hash-1',
    settings: 'min_days=20',
  },
  arms: {
    state: 'unavailable',
    reason: 'no stored scores to replay the arms on',
    candidates: 0,
    frozenMean: null,
    retrainedMean: null,
    noMlMean: null,
    retrainedVsFrozen: null,
    retrainedVsNoMl: null,
  },
  curve: {
    state: 'unavailable',
    reason: 'the learning-curve refit loop is not built',
    verdict: null,
    pointsUsed: 0,
    slopePer50Rows: null,
    standardError: null,
    projectedRows: null,
    moreRows: null,
    text: '',
  },
  slope: {
    state: 'unavailable',
    reason: 'no stored scores',
    verdict: null,
    outcomes: 0,
    slope: null,
    intercept: null,
    standardError: null,
    text: '',
  },
  lift: {
    state: 'unavailable',
    reason: 'no stored scores',
    topTrades: 0,
    bottomTrades: 0,
    lift: null,
    standardError: null,
    liftZ: null,
    approvedTickets: 0,
    approvedInTop: 0,
    approvedZ: null,
    text: '',
  },
  statements: ['Nothing here is a forecast.'],
  line: 'Loop: nothing can be judged yet.',
  ...over,
});

/** Every section available, with the interval and the standard error of the curve. */
export const buildFullLoopReport = (): LoopReport =>
  buildLoopReport({
    arms: {
      state: 'available',
      reason: null,
      candidates: 240,
      frozenMean: 0.02,
      retrainedMean: 0.05,
      noMlMean: 0.01,
      retrainedVsFrozen: {
        name: 'retrained_vs_frozen',
        verdict: 'helps',
        candidates: 240,
        days: 41,
        meanDifference: 0.03,
        low: 0.005,
        high: 0.055,
        probabilityBetter: 0.97,
        probabilityWorse: 0.03,
        text: 'Retraining helps: P(better) 97%.',
      },
      retrainedVsNoMl: {
        name: 'retrained_vs_no_ml',
        verdict: 'inconclusive',
        candidates: 240,
        days: 12,
        meanDifference: null,
        low: null,
        high: null,
        probabilityBetter: null,
        probabilityWorse: null,
        text: 'Fewer days than the minimum.',
      },
    },
    curve: {
      state: 'available',
      reason: null,
      verdict: 'unclear',
      pointsUsed: 3,
      slopePer50Rows: 0.0045,
      standardError: null,
      projectedRows: 204,
      moreRows: 54,
      text: 'A projection of three points, not a finding.',
    },
    slope: {
      state: 'available',
      reason: null,
      verdict: 'alarm',
      outcomes: 220,
      slope: 0.45,
      intercept: 0,
      standardError: 0.1,
      text: 'Overconfident: slope 0.45.',
    },
    lift: {
      state: 'available',
      reason: null,
      topTrades: 20,
      bottomTrades: 20,
      lift: 0.3,
      standardError: 0.3795,
      liftZ: 0.79,
      approvedTickets: 30,
      approvedInTop: 21,
      approvedZ: 2.19,
      text: 'Lift 0.30 R, not significant.',
    },
  });

export const buildProposal = (over: Partial<Proposal> = {}): Proposal => ({
  id: 7,
  strategy: 'orb',
  rule: 'cost_stress',
  proposalOnly: false,
  value: -0.04,
  limit: 0,
  trades: 130,
  text: 'Net R at 2x costs is -0.04.',
  cells: ['orb|15m|BUY|intraday|fixed_r|*'],
  settingsHash: 'kill-hash-1',
  proposedOn: '2026-11-04',
  proposedAt: '2026-11-04T04:00:00Z',
  state: 'proposed',
  actor: null,
  decidedAt: null,
  autoSettingId: null,
  ...over,
});

export const buildProposals = (over: Partial<Proposals> = {}): Proposals => ({
  items: [],
  settings: { auto: false, settingId: null, stressMultiplier: 2, drawdownFactor: 2, settingsHash: 'kill-set-1' },
  statements: ["Applying a proposal is the owner's act."],
  ...over,
});

export interface ConfidenceFake {
  decay?: DecayReport;
  loop?: LoopReport;
  proposals?: Proposals;
}

/**
 * Mounts the three confidence reads. A decay strategy other than the report's answers 404; every request is recorded in
 * `methods` so a test can show that the tabs only read.
 */
export function mountConfidence(fake: ConfidenceFake = {}): { methods: string[]; strategies: string[] } {
  const seen = { methods: [] as string[], strategies: [] as string[] };
  server.events.on('request:start', ({ request }) => {
    seen.methods.push(request.method);
  });
  server.use(
    http.get('/api/confidence/decay', ({ request }) => {
      const strategy = new URL(request.url).searchParams.get('strategy') ?? '';
      seen.strategies.push(strategy);
      const report = fake.decay ?? buildDecayReport();
      return strategy === report.strategy
        ? HttpResponse.json(report)
        : problemResponse({ title: 'The strategy has no series.', code: 'paper.not_found' }, 404);
    }),
    http.get('/api/confidence/loop', () => HttpResponse.json(fake.loop ?? buildLoopReport())),
    http.get('/api/confidence/kill-proposals', () => HttpResponse.json(fake.proposals ?? buildProposals())),
  );
  return seen;
}
