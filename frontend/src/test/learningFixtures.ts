import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { problemResponse, server } from './server';

type Cell = Schemas['CellStatsView'];
type Overview = Schemas['LearningOverviewView'];
type Section = Schemas['OverviewSection'];
type Entry = Schemas['ChangelogEntryView'];
type State = Schemas['CellStateView'];

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
