import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { problemResponse, server } from './server';

type List = Schemas['ScorecardListView'];
type Summary = Schemas['ScorecardSummaryView'];
type Card = Schemas['ScorecardView'];
type Row = Schemas['ScorecardRowView'];
type Sub = Schemas['SubScoreView'];
type Gate = Schemas['ReadinessGateView'];
type Settings = Schemas['ReadinessSettingsView'];

const names = [
  'Out-of-sample sample size',
  'Lower bound of the effect',
  'Expectancy after the trials factor',
  'Walk-forward folds',
  'Sign stable by month',
  'Deflated Sharpe',
  'Costs at twice the model',
  'Hold-out unlocked once',
  'Registered before the run',
  'Concentration by quarter and instrument',
  'Monte Carlo 90th percentile',
  'Cost confirmation',
  'Benchmark',
  'Capacity and tier',
  'Phase check',
];

/** One checklist row. Today's reality is `unavailable`: the value then holds the reason. */
export const buildRow = (number: number, over: Partial<Row> = {}): Row => ({
  number,
  name: names[number - 1] ?? `Row ${String(number)}`,
  producer: `CF-${String(number).padStart(2, '0')} producer`,
  status: 'unavailable',
  threshold: `threshold ${String(number)}`,
  value: `no producer yet for row ${String(number)}`,
  ...over,
});

/** All 15 rows unavailable: what the screen shows today. */
export const unavailableRows = (): Row[] => Array.from({ length: 15 }, (_, i) => buildRow(i + 1));

/** Rows 1 to 4 pass, row 5 fails, the rest are not available. */
export const mixedRows = (): Row[] =>
  unavailableRows().map((row) =>
    row.number <= 4
      ? { ...row, status: 'pass', value: `measured ${String(row.number)}.25`, threshold: 'at least 0.15' }
      : row.number === 5
        ? { ...row, status: 'fail', value: '41 % of months positive' }
        : row,
  );

const subNames = ['Evidence', 'Effect size', 'Power', 'Stability', 'Calibration', 'Paper agreement'];

export const unavailableSubs = (): Sub[] =>
  subNames.map((name) => ({
    name,
    state: 'unavailable',
    score: null,
    reading: `${name} has no input yet`,
    whatWouldMoveIt: null,
  }));

export const scoredSubs = (): Sub[] =>
  subNames.map((name, i) => ({
    name,
    state: 'scored',
    score: [60.7, 25, 20.2, 50, 40, 100][i] ?? 0,
    reading: `${name} reading`,
    whatWouldMoveIt: i === 2 ? 'Power 38.08 %: 12 more effective trades reach 50' : null,
  }));

const gate = (g: 'R' | 'S', over: Partial<Gate> = {}): Gate => ({
  gate: g,
  passed: false,
  labelOk: g === 'R',
  failing: [buildRow(10), buildRow(11)],
  ...over,
});

export const buildSummary = (over: Partial<Summary> = {}): Summary => ({
  strategy: 'orb',
  version: 3,
  score: 0,
  label: 'not_ready',
  labelText: 'Not ready',
  weakestSubScore: 'Evidence',
  rowsPassed: 0,
  hardFails: ['Integrity check unavailable'],
  gateR: false,
  gateS: false,
  ...over,
});

export const buildCard = (over: Partial<Card> = {}): Card => ({
  strategy: 'orb',
  version: 3,
  score: 0,
  rawMinimum: 0,
  label: 'not_ready',
  labelText: 'Not ready',
  subScores: unavailableSubs(),
  hardFails: ['Integrity check unavailable'],
  notApplicable: [],
  weakestSubScore: 'Evidence',
  whatWouldMoveIt: null,
  rows: unavailableRows(),
  rowsPassed: 0,
  gateR: gate('R'),
  gateS: gate('S'),
  waiting: ['row 13: the benchmark (P8-07)', 'row 14: capacity and tier'],
  notes: ['The thresholds are proposals.'],
  enforce: false,
  settingsHash: 'abc123hash',
  settings: 'anchors: 15/30/100',
  history: [{ id: 41, date: '2026-11-05', score: 0, label: 'not_ready', settingsHash: 'abc123hash' }],
  ...over,
});

export const buildSettings = (over: Partial<Settings> = {}): Settings => ({
  enforce: false,
  switchId: null,
  settingsHash: 'abc123hash',
  settings: 'anchors: 15/30/100',
  ...over,
});

export const buildList = (over: Partial<List> = {}): List => ({
  system: {
    score: 0,
    label: 'not_ready',
    labelText: 'Not ready',
    integrity: 0,
    weightedMean: 0,
    equalWeights: true,
    weakestStrategy: 'orb',
    weakestSubScore: 'Evidence',
    strategies: 2,
    reason: 'The integrity checks are not verified.',
  },
  strategies: [buildSummary(), buildSummary({ strategy: 'vwap', version: 1 })],
  settings: buildSettings(),
  statements: ['Derived on every read; no order exists.'],
  ...over,
});

export interface ReadinessFake {
  list?: List;
  /** Cards by `strategy@version`; the default card is answered for `orb@3`, any other is a 404. */
  cards?: Record<string, Card>;
  settings?: Settings;
  /** How the PUT answers. */
  put?: 'ok' | 'wrong-password' | 'step-up' | 'invalid';
}

export interface ReadinessSeen {
  cardRequests: string[];
  puts: Record<string, unknown>[];
  methods: string[];
}

/** Mounts the readiness endpoints. A successful PUT flips the switch the next read returns. */
export function mountReadiness(fake: ReadinessFake = {}): ReadinessSeen {
  const seen: ReadinessSeen = { cardRequests: [], puts: [], methods: [] };
  let settings = fake.settings ?? buildSettings();
  const cards = fake.cards ?? { 'orb@3': buildCard() };
  server.events.on('request:start', ({ request }) => {
    seen.methods.push(request.method);
  });
  server.use(
    http.get('/api/confidence/scorecards', () => HttpResponse.json(fake.list ?? buildList({ settings }))),
    http.get('/api/confidence/scorecards/:strategy/versions/:version', ({ params }) => {
      const key = `${String(params.strategy)}@${String(params.version)}`;
      seen.cardRequests.push(key);
      const card = cards[key];
      return card
        ? HttpResponse.json(card)
        : problemResponse({ title: 'No scorecard for this strategy.', code: 'confidence.scorecard_not_found' }, 404);
    }),
    http.get('/api/confidence/readiness-settings', () => HttpResponse.json(settings)),
    http.put('/api/confidence/readiness-settings', async ({ request }) => {
      const body = (await request.json()) as { enforce: boolean };
      seen.puts.push(body);
      switch (fake.put ?? 'ok') {
        case 'wrong-password':
          return problemResponse({ title: 'The password is wrong.', code: 'auth.invalid_credentials' }, 401);
        case 'step-up':
          return problemResponse({ title: 'A fresh password is required.', code: 'auth.step_up_required' }, 403);
        case 'invalid':
          return problemResponse({ title: 'The reason is not valid.', code: 'confidence.reason_invalid' }, 400);
        case 'ok':
          settings = { ...settings, enforce: body.enforce, switchId: 7 };
          return HttpResponse.json({ id: 7, enforce: body.enforce });
      }
    }),
  );
  return seen;
}
