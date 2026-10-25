import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { problemResponse, server } from './server';

type Cell = Schemas['CellStatsView'];
type Overview = Schemas['LearningOverviewView'];
type Section = Schemas['OverviewSection'];
type Entry = Schemas['ChangelogEntryView'];
type State = Schemas['CellStateView'];
type Lesson = Schemas['LessonView'];
type Experiment = Schemas['ExperimentView'];
type Hypothesis = Schemas['HypothesisView'];
type Model = Schemas['ModelView'];
type ModelDetail = Schemas['ModelDetailView'];
type Digest = Schemas['DigestView'];

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
  /** Window codes the screen asked for, in order. */
  asked: string[];
}

/** Mounts the five learning reads. Anything not given answers empty; a state not given answers research without rows. */
export function mountLearning(fake: Omit<LearningFake, 'asked'> = {}): LearningFake {
  const state: LearningFake = { ...fake, asked: [] };
  const cellsOf = (window: string) => fake.cells?.[window] ?? fake.cells?.all ?? [];
  server.use(
    http.get('/api/learning/overview', () => HttpResponse.json(fake.overview ?? buildOverview())),
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

export const lessonNote = 'Linking or closing it changes nothing in the system.';

/** An open lesson with no fix tried. */
export const buildLesson = (over: Partial<Lesson> = {}): Lesson => ({
  id: 1,
  state: 'open',
  monitorKind: 'rolling_band',
  subject: 'orb|15m|BUY|intraday|fixed_r|*',
  evidenceKey: 'band:orb',
  problem: 'Expectancy fell below its band (-0.12 R over 40 trades).',
  hypothesisRef: null,
  experimentId: null,
  result: null,
  experimentOutcome: null,
  beforeMeanR: null,
  afterMeanR: null,
  beforeTotalR: null,
  afterTotalR: null,
  reason: 'Opened by the monitor.',
  actor: 'rule:monitor',
  createdAt: '2026-10-20T04:30:00Z',
  updatedAt: '2026-10-21T04:30:00Z',
  nextStates: ['linked', 'dismissed'],
  history: [
    { rowId: 1, state: 'open', reason: 'Opened by the monitor.', actor: 'rule:monitor', at: '2026-10-20T04:30:00Z' },
  ],
  note: lessonNote,
  ...over,
});

export const buildExperiment = (over: Partial<Experiment> = {}): Experiment => ({
  id: 7,
  experimentKey: 'abc123',
  knobName: 'orb.minRange',
  knobKind: 'Parameter',
  baselineValue: '0.5',
  candidateValue: '0.8',
  baselineRunKey: 'run-a',
  candidateRunKey: 'run-b',
  from: '2024-09-01',
  to: '2026-09-30',
  holdOutStart: '2026-07-01',
  outcome: 'NotBetter',
  trials: 4,
  usedFolds: 6,
  foldsBetter: 2,
  foldsNeeded: 5,
  requiredImprovementR: 0.03,
  baselineTrades: 120,
  candidateTrades: 118,
  pooledBaselineMeanR: -0.1,
  pooledCandidateMeanR: 0.05,
  baselineTotalR: -12,
  candidateTotalR: 5.9,
  expectedLiveGapR: 0.05,
  compareMs: 1200,
  reasons: [],
  reportText: 'Fold 1  baseline -0.10  candidate +0.05\nFold 2  baseline -0.08  candidate +0.01',
  parameters: 'folds=6; gap=0.05',
  createdAt: '2026-10-21T04:30:00Z',
  createdBy: 'cli',
  ...over,
});

export const buildHypothesis = (over: Partial<Hypothesis> = {}): Hypothesis => ({
  id: 3,
  state: 'proposed',
  source: 'owner',
  cellKey: 'orb|15m|BUY|intraday|fixed_r|*',
  knobName: 'orb.minRange',
  knobKind: 'Parameter',
  baselineValue: '0.5',
  candidateValue: '0.8',
  rationale: 'A wider opening range may cut false breaks.',
  baselineRunKey: 'run-a',
  candidateRunKey: null,
  experimentId: null,
  reason: 'Proposed.',
  actor: 'owner',
  createdAt: '2026-10-20T04:30:00Z',
  updatedAt: '2026-10-20T04:30:00Z',
  nextStates: ['approved', 'rejected'],
  history: [{ rowId: 1, state: 'proposed', reason: 'Proposed.', actor: 'owner', at: '2026-10-20T04:30:00Z' }],
  note: "Accepting it changes nothing: applying a change is the owner's own act.",
  ...over,
});

export const buildModel = (over: Partial<Model> = {}): Model => ({
  id: 2,
  sha256: 'f'.repeat(64),
  createdAt: '2026-10-19T04:30:00Z',
  createdBy: 'cli',
  trainingSetId: 'ts-1',
  featureVersion: 'v3',
  trainer: 'FastTree',
  note: null,
  fitRows: 800,
  calibrationRows: 200,
  trainRows: 1000,
  from: '2024-09-01',
  to: '2026-06-30',
  holdOutStart: '2026-07-01',
  verdict: 'Eligible',
  usedFolds: 6,
  foldsBeatingBoth: 5,
  foldsNeeded: 5,
  pooledBeatsBoth: true,
  pooledEcePp: 2.5,
  pooledLogLoss: 0.65,
  pooledBrier: 0.23,
  reasons: [],
  role: 'challenger',
  mayBeChampion: true,
  ...over,
});

export const buildModelDetail = (over: Partial<ModelDetail> = {}): ModelDetail => ({
  model: buildModel(),
  parameters: 'trees=100; leaves=20',
  reportText: 'Fold table\nfold 1  beat both',
  calibration: [
    { score: -1, probability: 0.3 },
    { score: 1, probability: 0.7 },
  ],
  importance: [{ feature: 'atr_ratio', meanAbsContribution: 0.31 }],
  designations: [
    { id: 1, role: 'challenger', reason: 'Registered.', createdAt: '2026-10-19T04:30:00Z', createdBy: 'cli' },
  ],
  limits: ['Advisory: a model changes no plan.'],
  ...over,
});

export const buildDigest = (over: Partial<Digest> = {}): Digest => ({
  id: 5,
  isoWeek: '2026-W43',
  weekStart: '2026-10-19',
  weekEnd: '2026-10-25',
  sections: [
    { key: 'cells', title: 'Cells', lines: ['2 new cells.'] },
    { key: 'plans', title: 'Plan outcomes (simulated on plan prices)', lines: ['3 plans graded.'] },
  ],
  text: 'Digest',
  builtBy: 'service:weekly',
  createdAt: '2026-10-25T10:00:00Z',
  repeated: false,
  ...over,
});

export interface RecordsFake {
  lessons?: Lesson[];
  experiments?: Experiment[];
  hypotheses?: Hypothesis[];
  models?: Model[];
  modelDetails?: ModelDetail[];
  digests?: Digest[];
}

/** Mounts the record reads of P8-03: lists and details by id; an id not given answers 404. */
export function mountRecords(fake: RecordsFake = {}) {
  const byId = (rows: { id: number }[] | undefined, id: unknown, code: string) => {
    const found = (rows ?? []).find((r) => String(r.id) === String(id));
    return found ? HttpResponse.json(found) : problemResponse({ title: 'Not found.', code }, 404);
  };
  const filtered = <T extends { state?: string; role?: string }>(rows: T[] | undefined, request: Request) => {
    const params = new URL(request.url).searchParams;
    const state = params.get('state');
    const role = params.get('role');
    return (rows ?? []).filter((r) => (!state || r.state === state) && (!role || r.role === role));
  };
  server.use(
    http.get('/api/lessons', ({ request }) => HttpResponse.json(filtered(fake.lessons, request))),
    http.get('/api/lessons/:id', ({ params }) => byId(fake.lessons, params.id, 'learning.lesson_not_found')),
    http.get('/api/experiments', () => HttpResponse.json(fake.experiments ?? [])),
    http.get('/api/experiments/:id', ({ params }) =>
      byId(fake.experiments, params.id, 'learning.experiment_not_found'),
    ),
    http.get('/api/hypotheses', ({ request }) => HttpResponse.json(filtered(fake.hypotheses, request))),
    http.get('/api/hypotheses/:id', ({ params }) => byId(fake.hypotheses, params.id, 'learning.hypothesis_not_found')),
    http.get('/api/models', ({ request }) => HttpResponse.json(filtered(fake.models, request))),
    http.get('/api/models/:id', ({ params }) => {
      const found = (fake.modelDetails ?? []).find((d) => String(d.model.id) === String(params.id));
      return found ? HttpResponse.json(found) : problemResponse({ title: 'Not found.', code: 'models.not_found' }, 404);
    }),
    http.get('/api/learning/digests', () => HttpResponse.json(fake.digests ?? [])),
    http.get('/api/learning/digest', ({ request }) => {
      const week = new URL(request.url).searchParams.get('week');
      const found = (fake.digests ?? []).find((d) => d.isoWeek === week);
      return found
        ? HttpResponse.json(found)
        : problemResponse({ title: 'No digest.', code: 'learning.digest_not_found' }, 404);
    }),
  );
}
