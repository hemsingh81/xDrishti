import { http, HttpResponse } from 'msw';
import type {
  AggregatePhase,
  CsvPhase,
  DataCoverageRow,
  DataJob,
  DataOverview,
  DhanPhase,
  StartDataJobInput,
} from '@/shared/api';
import {
  buildAggregatePhase,
  buildCsvPhase,
  buildDataJob,
  buildDataOverview,
  buildDhanPhase,
  buildInstrumentBars,
  dataJobId,
  sampleCoverage,
  searchCoverage,
} from './fixtures';
import { problemResponse } from './server';

type PhaseKey = 'csv' | 'dhan' | 'aggregate';
const order: readonly PhaseKey[] = ['csv', 'dhan', 'aggregate'];

export interface DataFakeOptions {
  overview?: DataOverview;
  coverage?: readonly DataCoverageRow[];
  /** Finished jobs, newest first. */
  jobs?: readonly DataJob[];
  /** A job already running when the page opens; it moves on a step each time the overview is read. */
  running?: { steps?: PhaseKey[] } | null;
  /** The phase that fails when a job runs (its message and code are below). */
  failPhase?: PhaseKey | null;
  /** The next start answers this problem instead of a job. */
  startError?: 'job_running' | 'master_missing' | 'server' | null;
  /** The job moves on by itself each time the overview is read (default). Off: only `fake.advance()` moves it. */
  autoAdvance?: boolean;
  /** What the stored data looks like once a job has finished (default: unchanged). */
  afterRun?: { overview?: Partial<DataOverview>; coverage?: readonly DataCoverageRow[] };
}

export const failureMessage = 'Dhan refused the access token.';
export const failureCode = 'data.dhan_not_connected';

/** Reads that can be made to fail: set a flag, then clear it to let a retry succeed. */
export interface DataFailures {
  overview: boolean;
  coverage: boolean;
  bars: boolean;
  jobs: boolean;
}

const unitsOf = (overview: DataOverview, key: PhaseKey) => (key === 'csv' ? overview.csv.filesTotal : 5);

/**
 * A stateful stand-in for the Data & import API. The overview, the coverage table (search, status, sort, paging), the
 * bars per timeframe and the runs work over one in-memory state. A job started with POST /api/data/refresh moves on one
 * step each time the overview is read: every requested phase is `running` (empty, then half way) and then ends; so a
 * test that polls sees the whole run. `calls` records what was asked; `failures` makes reads fail.
 */
export function createDataFake(options: DataFakeOptions = {}) {
  let overview = options.overview ?? buildDataOverview();
  let coverage: readonly DataCoverageRow[] = options.coverage ?? sampleCoverage;
  let history: DataJob[] = [...(options.jobs ?? [buildDataJob()])];
  let running: DataJob | null = null;
  let cursor = { phase: 0, step: 0 };
  let nextJob = 10;
  let startError = options.startError ?? null;
  const calls = {
    start: [] as StartDataJobInput[],
    coverage: [] as string[],
    bars: [] as { id: string; timeframe: string; take: string | null }[],
    overview: 0,
  };
  const failures: DataFailures = { overview: false, coverage: false, bars: false, jobs: false };
  const serverError = () => problemResponse({ title: 'The data service is not available.', code: 'data.down' }, 500);

  const newJob = (body: StartDataJobInput | null, id: string): DataJob => {
    const steps = order.filter((key) => (body?.steps ?? order).includes(key));
    const phase = <P extends { status: string }>(key: PhaseKey, base: P): P => {
      const on = steps.includes(key);
      const total = unitsOf(overview, key);
      return {
        ...base,
        status: on ? (key === steps[0] ? 'running' : 'pending') : 'notRequested',
        startedAt: null,
        finishedAt: null,
        done: 0,
        total: on ? total : 0,
        message: null,
        code: null,
      };
    };
    return buildDataJob({
      id,
      status: 'running',
      startedAt: '2026-10-12T16:00:00+05:30',
      finishedAt: null,
      steps,
      forceCsv: body?.forceCsv ?? false,
      currentPhase: steps[0] ?? null,
      csv: phase('csv', buildCsvPhase({ rows: 0, inserted: 0, repaired: 0 })),
      dhan: phase('dhan', buildDhanPhase({ bars: 0, inserted: 0 })),
      aggregate: phase('aggregate', buildAggregatePhase({ buckets: 0 })),
    });
  };

  if (options.running) {
    running = newJob({ steps: options.running.steps ?? [...order] }, dataJobId(2));
  }

  const withPhase = (job: DataJob, key: PhaseKey, patch: Partial<CsvPhase & DhanPhase & AggregatePhase>): DataJob => ({
    ...job,
    [key]: { ...job[key], ...patch },
  });

  /** What a finished job leaves in the overview. */
  const settle = (job: DataJob) => {
    const done = (key: PhaseKey) => job[key].status === 'succeeded';
    overview = {
      ...overview,
      ...options.afterRun?.overview,
      csv: done('csv')
        ? {
            ...overview.csv,
            filesLoaded: overview.csv.filesTotal,
            filesPending: 0,
            lastLoadAt: '2026-10-12T16:05:00+05:30',
          }
        : overview.csv,
      dhan: done('dhan')
        ? { ...overview.dhan, lastFetchAt: '2026-10-12T16:10:00+05:30', lastFetchBars: job.dhan.bars }
        : overview.dhan,
      aggregates: done('aggregate')
        ? { ...overview.aggregates, lastBuiltAt: '2026-10-12T16:15:00+05:30', instrumentsPending: 0 }
        : overview.aggregates,
      lastJobs: {
        csv: job.csv.status === 'notRequested' ? overview.lastJobs.csv : job,
        dhan: job.dhan.status === 'notRequested' ? overview.lastJobs.dhan : job,
        aggregate: job.aggregate.status === 'notRequested' ? overview.lastJobs.aggregate : job,
      },
    };
    if (options.afterRun?.coverage) coverage = options.afterRun.coverage;
    history = [job, ...history];
  };

  /** One step: the phase in turn goes from empty to half way to its end, then the next phase starts. */
  const advance = () => {
    if (!running) return;
    const steps = order.filter((key) => running?.steps.includes(key));
    const key = steps[cursor.phase];
    if (!key) return;
    let job = running;
    const total = job[key].total;
    if (cursor.step === 0) {
      job = withPhase(job, key, {
        startedAt: '2026-10-12T16:00:00+05:30',
        done: Math.floor(total / 2),
        message: key === 'csv' ? 'Loading RELIANCE_1m.csv.gz' : `Working on ${key}`,
      });
      cursor = { ...cursor, step: 1 };
      running = job;
      return;
    }
    const failed = options.failPhase === key;
    const skipped = key === 'dhan' && !overview.dhan.canFetch && !failed;
    job = withPhase(job, key, {
      status: failed ? 'failed' : skipped ? 'skipped' : 'succeeded',
      finishedAt: '2026-10-12T16:05:00+05:30',
      done: failed || skipped ? 0 : total,
      message: failed ? failureMessage : skipped ? overview.dhan.reason : null,
      code: failed ? failureCode : skipped ? overview.dhan.reasonCode : null,
      ...(key === 'dhan' && !failed && !skipped ? { bars: 4_500 } : {}),
    });
    const next = steps[cursor.phase + 1];
    if (failed || !next) {
      const rest = steps.slice(cursor.phase + 1);
      for (const later of rest) {
        job = withPhase(job, later, { status: 'skipped', message: 'An earlier step failed.', code: null });
      }
      running = null;
      settle({
        ...job,
        status: failed ? 'failed' : 'succeeded',
        currentPhase: null,
        finishedAt: '2026-10-12T16:05:00+05:30',
        error: failed ? failureMessage : null,
      });
      return;
    }
    cursor = { phase: cursor.phase + 1, step: 0 };
    running = withPhase({ ...job, currentPhase: next }, next, { status: 'running' });
  };

  const handlers = [
    http.get('/api/data/overview', () => {
      calls.overview += 1;
      if (failures.overview) return serverError();
      if (options.autoAdvance !== false) advance();
      return HttpResponse.json({ ...overview, runningJob: running });
    }),
    http.get('/api/data/instruments', ({ request }) => {
      const url = new URL(request.url);
      calls.coverage.push(url.search);
      if (failures.coverage) return serverError();
      return HttpResponse.json(searchCoverage(coverage, url.searchParams));
    }),
    http.get('/api/data/instruments/:id/bars', ({ params, request }) => {
      const url = new URL(request.url);
      const timeframe = url.searchParams.get('timeframe') ?? '1m';
      calls.bars.push({ id: String(params.id), timeframe, take: url.searchParams.get('take') });
      if (failures.bars) return serverError();
      const row = coverage.find((r) => r.id === params.id);
      if (!row)
        return problemResponse({ title: 'The instrument does not exist.', code: 'data.instrument_not_found' }, 404);
      return HttpResponse.json({
        ...buildInstrumentBars(row.symbol, timeframe),
        instrumentId: row.id,
        name: row.name,
        // The sample has no weekly or monthly bars yet, to show the empty state.
        ...(timeframe === '1M' ? { items: [] } : {}),
      });
    }),
    http.post('/api/data/refresh', async ({ request }) => {
      const body = (await request.json().catch(() => null)) as StartDataJobInput | null;
      calls.start.push(body ?? {});
      const error = startError;
      startError = null;
      if (running || error === 'job_running') {
        return problemResponse({ title: 'An update is already running.', code: 'data.job_running' }, 409);
      }
      if (error === 'master_missing') {
        return problemResponse({ title: 'The instrument list is missing.', code: 'data.master_missing' }, 409);
      }
      if (error === 'server') return serverError();
      running = newJob(body, dataJobId(nextJob++));
      cursor = { phase: 0, step: 0 };
      return HttpResponse.json(running, { status: 202 });
    }),
    http.get('/api/data/jobs', ({ request }) => {
      if (failures.jobs) return serverError();
      const take = Number(new URL(request.url).searchParams.get('take') ?? 10);
      return HttpResponse.json([...(running ? [running] : []), ...history].slice(0, take));
    }),
    http.get('/api/data/jobs/:id', ({ params }) => {
      const job = [...(running ? [running] : []), ...history].find((j) => j.id === params.id);
      return job
        ? HttpResponse.json(job)
        : problemResponse({ title: 'The job does not exist.', code: 'data.job_not_found' }, 404);
    }),
  ];

  return {
    handlers,
    calls,
    /** Moves the job in progress one step on (for tests that look at each step). */
    advance,
    failures,
    /** The job in progress, if any. */
    get running() {
      return running;
    },
    /** The overview as the next read would answer it (without moving the job on). */
    get overview() {
      return overview;
    },
  };
}

export type DataFake = ReturnType<typeof createDataFake>;
