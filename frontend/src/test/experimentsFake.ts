import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { buildExperiment } from './learningFixtures';
import { buildRun, buildRunsView, runKeyA } from './reportFixtures';
import { problemResponse, server } from './server';
import { createCapabilitiesFake } from './capabilitiesFake';

type Preview = Schemas['ExperimentPreviewView'];
type Job = Schemas['ExperimentJobView'];

export const jobId = '11111111-2222-4333-8444-555555555555';
export const closingSentence = 'Nothing was changed. Applying a change is your own separate act.';
export const noTrial = 'Judged nothing; not counted as a trial.';

export const buildPreview = (over: Partial<Preview> = {}): Preview => ({
  baseline: { strategyId: 'orb', from: '2024-04-01', to: '2026-04-30', instruments: 12, costed: true, exit: 'fixed_r' },
  rebuildable: true,
  notRebuildableReason: null,
  knobs: [
    { kind: 'exit', name: 'FIXED_RR.rr', current: 2, min: 1, max: 4, note: 'Reward to risk of the target.' },
    { kind: 'slippage', name: 'slippage.ticks', current: 1, min: 1, max: 5, note: 'Only upward.' },
  ],
  nextTrial: 4,
  requiredImprovementR: 0.032,
  foldsAvailable: 3,
  minFoldsForBetter: 6,
  bestPossibleOutcome: 'Inconclusive',
  bestPossibleSentence: 'Only 3 folds exist and 6 are needed, so the best this can be is Inconclusive.',
  replayEstimate: { state: 'not_evaluable', ms: null, text: 'No replay time measured yet.' },
  jobsToday: 1,
  jobsPerDay: 3,
  activeJobId: null,
  limits: ['A run is a look: the knob was picked by looking at the same history.'],
  ...over,
});

export const buildJob = (over: Partial<Job> = {}): Job => ({
  id: jobId,
  state: 'running',
  stage: 'replay',
  done: 3,
  total: 12,
  baselineRunKey: runKeyA,
  knob: { kind: 'exit', name: 'FIXED_RR.rr', baselineValue: '2', candidateValue: '3' },
  candidateRunKey: null,
  experimentId: null,
  errorCode: null,
  errorText: null,
  queuedAt: '2026-10-26T06:00:00Z',
  startedAt: '2026-10-26T06:00:01Z',
  finishedAt: null,
  replayMs: null,
  compareMs: null,
  instruments: 12,
  otherHeavyJobsSeen: false,
  cancelRequested: false,
  timing: null,
  limits: [closingSentence],
  ...over,
});

export interface ExperimentsFake {
  preview: Preview;
  job: Job;
  /** Every body POSTed to /api/experiments. */
  starts: Record<string, unknown>[];
  cancels: number;
  jobReads: number;
  /** Answers every start with this problem. */
  refuse: { status: number; code: string; title: string; extra?: Record<string, unknown> } | null;
  judgement: Schemas['ExperimentView'];
}

/** The experiment routes and the capability list (switched ON unless `on` is false), as a stateful stand-in. */
export function mountExperiments({ on = true, ...over }: Partial<ExperimentsFake> & { on?: boolean } = {}) {
  const caps = createCapabilitiesFake();
  caps.state.items = caps.state.items.map((i) => (i.name === 'experiments_ui' ? { ...i, enabled: on } : i));
  const state: ExperimentsFake = {
    preview: buildPreview(),
    job: buildJob(),
    starts: [],
    cancels: 0,
    jobReads: 0,
    refuse: null,
    judgement: buildExperiment({
      id: 9,
      outcome: 'Inconclusive',
      foldsBetter: 3,
      usedFolds: 3,
      foldsNeeded: 6,
      trials: 4,
    }),
    ...over,
  };
  server.use(
    ...caps.handlers,
    http.get('/api/reports/runs', () =>
      HttpResponse.json(
        buildRunsView([
          buildRun(),
          buildRun({
            runKey: 'c'.repeat(64),
            strategyId: 'orb',
            defaultSetNote: 'An experiment candidate: left out of the default set.',
          }),
        ]),
      ),
    ),
    http.get('/api/experiments/preview', () => HttpResponse.json(state.preview)),
    http.post('/api/experiments', async ({ request }) => {
      const body = (await request.json()) as Record<string, unknown>;
      state.starts.push(body);
      if (state.refuse) {
        return problemResponse(
          { title: state.refuse.title, code: state.refuse.code, status: state.refuse.status, ...state.refuse.extra },
          state.refuse.status,
        );
      }
      if (body.password === 'wrong') {
        return problemResponse({ title: 'Invalid credentials', code: 'auth.invalid_credentials', status: 401 }, 401);
      }
      return HttpResponse.json(state.job, { status: 202 });
    }),
    http.get('/api/experiments/jobs/:id', () => {
      state.jobReads += 1;
      return HttpResponse.json(state.job);
    }),
    http.post('/api/experiments/jobs/:id/cancel', () => {
      state.cancels += 1;
      state.job = buildJob({
        state: 'cancelled',
        stage: null,
        errorText: 'Cancelled by the owner.',
        limits: [noTrial, closingSentence],
      });
      return HttpResponse.json(state.job, { status: 202 });
    }),
    http.get('/api/experiments/:id', () => HttpResponse.json(state.judgement)),
  );
  return state;
}
