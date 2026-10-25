import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { problemResponse, server } from './server';

type Cell = Schemas['CellStatsView'];
type Overview = Schemas['LearningOverviewView'];
type Section = Schemas['OverviewSection'];
type Entry = Schemas['ChangelogEntryView'];
type State = Schemas['CellStateView'];
type PlanVsActual = Schemas['PlanVsActualView'];
type Decisions = Schemas['GradeReportData'];
type Autonomy = Schemas['AutonomySettingsView'];
type PlanCheck = Schemas['PlanPhaseCheckView'];
type LearningCheck = Schemas['LearningPhaseCheckView'];
type Metric = Schemas['MetricView'];

export const metric = (number: number): Metric => ({ state: 'value', number, reason: null });
const gone = (state: string, reason: string): Metric => ({ state, number: null, reason });
const stepLabels: [string, string][] = [
  ['plan_prices', 'Plan prices'],
  ['entry_slippage', 'Entry slippage'],
  ['charges', 'Charges'],
  ['not_attributed', 'Not attributed (exit slippage and anything else)'],
  ['actual', 'Actual, after charges'],
];
const noFills = 'No ticket has been graded on fills yet; fills arrive with paper and live trading, P10/P11.';

/** Plans exist, no fill grade: the chain is empty, the counts are printed. */
export const buildPlanVsActual = (over: Partial<PlanVsActual> = {}): PlanVsActual => ({
  from: null,
  to: null,
  plans: 2,
  counts: {
    approvedLines: 3,
    withFills: 0,
    gradedOnFills: 0,
    uncertain: 0,
    noPlanLevels: 0,
    notFinished: 0,
    withoutFills: 3,
  },
  approvals: [
    { kind: 'owner', count: 2 },
    { kind: 'auto', count: 1 },
  ],
  chain: stepLabels.map(([step, label]) => ({ step, label, metric: gone('empty', noFills), n: 0 })),
  missed: { count: 0, totalPlanR: gone('empty', 'no approved ticket without a fill triggered') },
  notDecomposed: [
    { step: 'exit_slippage', reason: 'The plan grade stores no simulated exit price.' },
    { step: 'overrides', reason: 'Overrides are tickets; read them in Decisions.' },
  ],
  byStrategy: [],
  minTickets: 15,
  evidence: {
    basis: 'realised fills, not simulated, R after the charges the sync recorded, beside simulated on plan prices',
    n: 0,
    firstDate: '2026-10-01',
    lastDate: '2026-10-02',
    provisional: false,
    limits: ['No graded real trade exists before paper and live trading (P10/P11).'],
  },
  ...over,
});

/** The hand-worked four tickets of the spec (minimum 2): plan +0.75, slippage -0.0875, charges -0.0475, rest -0.045, actual +0.57. */
export const buildGradedPlanVsActual = (): PlanVsActual => {
  const values = [0.75, -0.0875, -0.0475, -0.045, 0.57];
  return buildPlanVsActual({
    counts: {
      approvedLines: 4,
      withFills: 4,
      gradedOnFills: 4,
      uncertain: 0,
      noPlanLevels: 0,
      notFinished: 0,
      withoutFills: 0,
    },
    chain: stepLabels.map(([step, label], i) => ({ step, label, metric: metric(values[i] ?? 0), n: 4 })),
    byStrategy: [
      { strategyId: 'ORB', n: 2, planR: metric(0.5), actualR: metric(0.34), entrySlippageR: metric(0.075) },
      { strategyId: 'VWAP', n: 2, planR: metric(1), actualR: metric(0.8), entrySlippageR: metric(0.1) },
    ],
    minTickets: 2,
    evidence: { ...buildPlanVsActual().evidence, n: 4 },
  });
};

export const buildDecisions = (over: Partial<Decisions> = {}): Decisions => ({
  basis: 'simulated on plan prices',
  plans: 0,
  curves: [],
  groups: [],
  decisions: [],
  comparisons: [],
  notes: [],
  ...over,
});

export const buildAutonomy = (over: Partial<Autonomy> = {}): Autonomy => ({
  id: null,
  hash: null,
  autoApproveTickets: false,
  promoteFromReserve: false,
  minPWin: 0.6,
  maxAutoPerDay: 3,
  notice: 'No order is placed in any setting.',
  history: [],
  ...over,
});

export const buildLearningCheck = (over: Partial<LearningCheck> = {}): LearningCheck => ({
  criteria: [
    {
      number: 1,
      name: 'A model beats the baselines',
      verdict: 'NOT EVALUABLE',
      detail: 'No model is stored.',
      caveat: null,
    },
    { number: 2, name: 'Calibration', verdict: 'NOT EVALUABLE', detail: 'No model is stored.', caveat: null },
  ],
  limits: ['This check reads stored records only.'],
  text: '',
  elapsedMs: 12,
  ...over,
});

export const buildPlanCheck = (over: Partial<PlanCheck['report']> = {}): PlanCheck => ({
  report: {
    through: '2026-10-23',
    streak: 0,
    exitVerdict: 'NOT EVALUABLE',
    exitCriterion: 'NOT EVALUABLE (fewer than ten sessions)',
    criteria: [{ number: 1, name: 'Ten sessions', verdict: 'NOT EVALUABLE', detail: 'No session yet.', caveat: null }],
    sessions: [],
    services: [],
    switches: [{ name: 'Automatic approval', on: false, effect: 'tickets wait for the owner', needed: false }],
    blockers: [],
    counts: [{ name: 'plans', value: 0 }],
    limits: ['A plan is advice; nothing is ordered.'],
    ...over,
  },
  text: '',
});

export const cellLimits =
  'Replayed history with modelled fills, about 2 years, in-sample on the data shown: a description of the past, not a forecast.';

export const cellKeyOf = (c: Pick<Cell, 'strategy' | 'timeframe' | 'side' | 'style' | 'exit'>) =>
  [c.strategy, c.timeframe, c.side, c.style, c.exit, '*'].join('|');

/** A cell with enough evidence (142 trades, minimum 30). */
export const buildCell = (over: Partial<Cell> = {}): Cell => {
  const base = {
    strategy: 'orb',
    timeframe: '15m',
    side: 'BUY',
    style: 'intraday',
    exit: 'fixed_r',
    ...over,
  };
  return {
    cellKey: cellKeyOf(base),
    regime: '*',
    window: 'all',
    asOf: '2026-10-20',
    basis: 'gross R (before charges)',
    n: 142,
    wins: 80,
    effectiveN: 96.4,
    winRateRaw: 0.563,
    priorRate: 0.5,
    winRatePosterior: 0.55,
    winRateLower: 0.482,
    winRateUpper: 0.61,
    avgWinR: 1.2,
    avgLossR: -1,
    expectancyR: 0.31,
    profitFactor: 1.6,
    maxDrawdownR: 6.5,
    maeP50: -0.4,
    maeP90: -0.9,
    mfeP50: 0.8,
    mfeP90: 1.9,
    enough: true,
    evidence: 'enough',
    recencyHalfLifeDays: 90,
    priorStrength: 10,
    minTrades: 30,
    credibleLevel: 0.95,
    snapshotKey: 'cells-2026-10-20',
    createdAt: '2026-10-20T17:00:00Z',
    limits: cellLimits,
    ...base,
    ...over,
  };
};

/**
 * A cell below the minimum. The server still stores numbers for it (expectancy 0.91, lower bound 0.337): the screen must
 * never print them, which is what the tests look for.
 */
export const buildThinCell = (over: Partial<Cell> = {}): Cell =>
  buildCell({
    n: 12,
    wins: 9,
    effectiveN: 7.5,
    winRateRaw: 0.75,
    winRatePosterior: 0.58,
    winRateLower: 0.337,
    winRateUpper: 0.82,
    expectancyR: 0.91,
    profitFactor: 4.4,
    maxDrawdownR: 1.3,
    enough: false,
    evidence: 'not enough evidence (fewer trades than the minimum)',
    ...over,
  });

const emptySection = (): Section => ({ counted: 0, truncated: false, split: [], newest: null, champion: null });

export const buildOverview = (over: Partial<Overview> = {}): Overview => ({
  cellRows: 0,
  cellsAsOf: null,
  models: emptySection(),
  experiments: emptySection(),
  hypotheses: emptySection(),
  lessons: emptySection(),
  changelog: emptySection(),
  limits: [
    'Counts are of the newest 200 records of each kind; a section marked truncated has more.',
    'Every verdict is as stored when the record was made: nothing is recomputed here.',
  ],
  ...over,
});

export const buildEntry = (over: Partial<Entry> = {}): Entry => ({
  id: 1,
  kind: 'transition',
  subjectKind: 'cell',
  subject: 'orb|15m|BUY|intraday|fixed_r|*',
  fromState: 'research',
  toState: 'candidate',
  reason: 'Passed the out-of-sample gates.',
  actor: 'rule:gates',
  evidenceIds: ['experiment:4'],
  beforeRef: null,
  afterRef: null,
  paperDays: null,
  revertsId: null,
  at: '2026-10-21T04:30:00Z',
  ...over,
});

export const buildCellState = (over: Partial<State> = {}): State => ({
  cell: 'orb|15m|BUY|intraday|fixed_r|*',
  state: 'research',
  legalNext: ['candidate', 'retired'],
  rows: 0,
  problems: [],
  paperDaysBeforeActive: 20,
  note: 'The state is a record; nothing acts on it before paper data and live exist.',
  ...over,
});

export interface LearningFake {
  overview?: Overview;
  /** Cells by window code (set each cell's `window` to match); a window not given answers with `all`'s cells. */
  cells?: Partial<Record<string, Cell[]>>;
  changelog?: Entry[];
  states?: Record<string, State>;
  planVsActual?: PlanVsActual;
  decisions?: Decisions;
  autonomy?: Autonomy;
  planCheck?: PlanCheck;
  learningCheck?: LearningCheck;
  /** Window codes the screen asked for, in order. */
  asked: string[];
}

/** Mounts the five learning reads. Anything not given answers empty; a state not given answers research without rows. */
export function mountLearning(fake: Omit<LearningFake, 'asked'> = {}): LearningFake {
  const state: LearningFake = { ...fake, asked: [] };
  const cellsOf = (window: string) => fake.cells?.[window] ?? fake.cells?.all ?? [];
  server.use(
    http.get('/api/learning/overview', () => HttpResponse.json(fake.overview ?? buildOverview())),
    http.get('/api/reports/plan-vs-actual', () => HttpResponse.json(fake.planVsActual ?? buildPlanVsActual())),
    http.get('/api/reports/decisions', () => HttpResponse.json(fake.decisions ?? buildDecisions())),
    http.get('/api/autonomy/settings', () => HttpResponse.json(fake.autonomy ?? buildAutonomy())),
    http.get('/api/plan-runs/phase-check', () => HttpResponse.json(fake.planCheck ?? buildPlanCheck())),
    http.get('/api/learning/phase-check', () => HttpResponse.json(fake.learningCheck ?? buildLearningCheck())),
    http.get('/api/cells', ({ request }) => {
      const window = new URL(request.url).searchParams.get('window') ?? 'all';
      state.asked.push(window);
      return HttpResponse.json(cellsOf(window));
    }),
    http.get('/api/cells/:key', ({ params }) => {
      const key = decodeURIComponent(String(params.key));
      const found = Object.values(fake.cells ?? {})
        .flatMap((cells) => cells ?? [])
        .filter((c) => c.cellKey === key);
      return found.length > 0
        ? HttpResponse.json(found)
        : problemResponse({ title: 'The cell is not in the newest snapshot.', code: 'learning.cell_not_found' }, 404);
    }),
    http.get('/api/changelog/cell-state', ({ request }) => {
      const cell = new URL(request.url).searchParams.get('cell') ?? '';
      return HttpResponse.json(fake.states?.[cell] ?? buildCellState({ cell }));
    }),
    http.get('/api/changelog', ({ request }) => {
      const params = new URL(request.url).searchParams;
      const kind = params.get('subjectKind');
      const subject = params.get('subject');
      return HttpResponse.json(
        (fake.changelog ?? []).filter((e) => (!kind || e.subjectKind === kind) && (!subject || e.subject === subject)),
      );
    }),
  );
  return state;
}
