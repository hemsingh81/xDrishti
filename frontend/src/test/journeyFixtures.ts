import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { mountRecords, type RecordsFake } from './learningFixtures';
import { problemResponse, server } from './server';

type Counterfactual = Schemas['CounterfactualReportView'];
type CounterfactualWindow = Schemas['CounterfactualWindowView'];
type Journey = Schemas['JourneyView'];
type Side = Schemas['CounterfactualSideView'];
type Entry = Schemas['ChangelogEntryView'];

const side = (over: Partial<Side> = {}): Side => ({
  logLoss: 0.69,
  brier: 0.25,
  auc: 0.55,
  takeR: 0.1,
  kept: 20,
  skipped: 20,
  skippedMeanR: -0.2,
  ...over,
});

export const buildWindow = (over: Partial<CounterfactualWindow> = {}): CounterfactualWindow => ({
  index: 2,
  start: '2026-04-01',
  end: '2026-06-30',
  rows: 40,
  days: 20,
  instruments: 5,
  frozenTrainRows: 100,
  retrainedTrainRows: 140,
  informative: true,
  frozen: side({ takeR: 0.1 }),
  retrained: side({ takeR: 0.16, logLoss: 0.68 }),
  gain: 0.01,
  gainStandardError: 0.004,
  takeRUplift: 2.4,
  cumulativeTakeRUplift: 2.4,
  note: null,
  ...over,
});

/** A stored counterfactual with a first window that is not judged, then uplifts of +2.4 R and -1.5 R (cumulative +0.9 R). */
export const buildCounterfactual = (over: Partial<Counterfactual> = {}): Counterfactual => ({
  schemaVersion: 1,
  id: 5,
  outcome: 'NoDifference',
  reasons: ['The pooled gain is inside its margin.'],
  cut: '2025-12-31',
  to: '2026-06-30',
  frozenAgeMonths: 6,
  frozenTrainRows: 100,
  knobs: {
    frozenMonths: 12,
    retrainMonths: 3,
    minWindowRows: 10,
    minJudgedWindows: 3,
    minGainLogLoss: 0.005,
    significanceZ: 1.645,
    trials: 1,
    policyTolerance: 0.01,
    holdOutMonths: 6,
  },
  windows: [
    buildWindow({
      index: 1,
      start: '2026-01-01',
      end: '2026-03-31',
      informative: false,
      gain: null,
      gainStandardError: null,
      takeRUplift: null,
      cumulativeTakeRUplift: 0,
      note: 'Same model on both sides, not judged.',
    }),
    buildWindow(),
    buildWindow({
      index: 3,
      start: '2026-07-01',
      end: '2026-09-30',
      rows: 50,
      frozen: side({ takeR: 0.12 }),
      retrained: side({ takeR: 0.09 }),
      takeRUplift: -1.5,
      cumulativeTakeRUplift: 0.9,
    }),
  ],
  judgedWindows: 3,
  judged: { rows: 130, frozen: side(), retrained: side(), gain: 0.004, gainStandardError: 0.003, takeRUplift: 0.9 },
  all: { rows: 170, frozen: side(), retrained: side(), gain: 0.003, gainStandardError: 0.003, takeRUplift: 0.9 },
  limits: ['Replayed history, not a forecast.', 'Model recipe: FastTree (p); training set ts-1.'],
  basis: 'gross',
  sources: [{ runKey: 'a'.repeat(64), style: 'intraday', exit: 'FIXED_RR' }],
  text: 'limit: x',
  builtBy: 'cli:xd',
  createdAt: '2026-10-25T04:30:00Z',
  sameRunsStored: 1,
  ...over,
});

export const liveReason = 'no graded real trade exists (paper fills arrive in P10, live in P11)';

export const buildJourney = (over: Partial<Journey> = {}): Journey => ({
  counterfactual: null,
  planPrice: { state: 'empty', weeks: [], totalGraded: 0, basis: 'simulated on plan prices', note: null },
  live: { state: 'not_evaluable', reason: liveReason, n: 0 },
  evidence: { basis: null, population: [], from: null, to: null, n: 0, limits: [] },
  ...over,
});

/** A journey whose newest counterfactual is `report`. */
export const journeyOf = (report: Counterfactual, over: Partial<Journey> = {}): Journey =>
  buildJourney({
    counterfactual: {
      id: report.id ?? 5,
      outcome: report.outcome,
      asOf: report.to,
      basis: report.basis,
      createdAt: report.createdAt ?? '2026-10-25T04:30:00Z',
      builtBy: 'cli:xd',
      windows: report.windows.length,
      judgedWindows: report.judgedWindows,
      sameRunsStored: report.sameRunsStored,
    },
    evidence: {
      basis: report.basis,
      population: report.sources.map((s) => s.runKey),
      from: report.cut,
      to: report.to,
      n: report.all?.rows ?? 0,
      limits: report.limits,
    },
    ...over,
  });

/** Mounts the Journey reads (P8-12): the journey, the counterfactual by id, and the lesson, model and changelog lists as given. */
export function mountJourney(
  journey: Journey,
  report: Counterfactual | null = null,
  records: RecordsFake = {},
  changelog: Entry[] = [],
) {
  mountRecords(records);
  server.use(
    http.get('/api/learning/journey', () => HttpResponse.json(journey)),
    http.get('/api/learning/counterfactual', () =>
      report
        ? HttpResponse.json(report)
        : problemResponse({ title: 'None.', code: 'learning.counterfactual_not_found' }, 404),
    ),
    http.get('/api/changelog', () => HttpResponse.json(changelog)),
  );
}
