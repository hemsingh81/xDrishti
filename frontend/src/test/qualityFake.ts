import { http, HttpResponse } from 'msw';
import type {
  DataJob,
  DhanOverview,
  QualityInstrumentDetail,
  QualityInstrumentRow,
  QualityOverview,
  QualityPhase,
  QualityRun,
} from '@/shared/api';
import {
  buildAggregatePhase,
  buildCsvPhase,
  buildDataJob,
  buildDataOverview,
  buildDhanOverview,
  buildDhanPhase,
  dataJobId,
} from './fixtures';
import {
  aggregateTimeframes,
  buildChecks,
  buildQualityDetail,
  buildQualityGate,
  buildQualityPhase,
  buildQualityRow,
  buildQualityRun,
  qualityRunId,
  sampleBuckets,
  sampleQualityRows,
} from './qualityFixtures';
import { problemResponse } from './server';

type PhaseKey = 'dhan' | 'aggregate' | 'quality';
const order: readonly PhaseKey[] = ['dhan', 'aggregate', 'quality'];

export type QualityStartError = 'job_running' | 'dhan_not_available' | 'dhan_not_connected' | 'not_found' | 'server';

export interface QualityFakeOptions {
  /** The instruments of the table (default: the samples). */
  rows?: readonly QualityInstrumentRow[];
  /** Past runs, newest first (default: one automatic run). `[]` = never run. */
  runs?: readonly QualityRun[];
  /** Details by instrument id; an instrument without one gets the generated detail of its row. */
  details?: Readonly<Record<string, QualityInstrumentDetail>>;
  /** Dhan as the data overview reports it (a re-fetch needs it to be available). */
  dhan?: Partial<DhanOverview>;
  /** A job already running when the page opens. */
  running?: { steps?: PhaseKey[]; instrumentId?: string } | null;
  /** The next start answers this problem instead of a job. */
  startError?: QualityStartError | null;
  /** The job moves on by itself each time the data overview is read (default). Off: only `fake.advance()` moves it. */
  autoAdvance?: boolean;
  /** The quality step of the next job fails with this message. */
  failQuality?: string | null;
  /** A fix action leaves the instrument as it was (the problem is still there). */
  fixLeavesIssues?: boolean;
  /** The rows once a run over every instrument has finished (default: the rows never checked now pass). */
  afterRun?: readonly QualityInstrumentRow[];
  /** Suggested corporate actions waiting for the owner (the overview reports them; the screen only has a slot). */
  pendingCorporateActions?: number;
  /** The maximum share of failing instruments before the gate blocks (default 5). */
  maxFailedPercent?: number;
  /** Sessions with missing bars the data overview reports as open (default 0). */
  openGaps?: number;
}

/** Reads that can be made to fail: set a flag, then clear it to let a retry succeed. */
export interface QualityFailures {
  overview: boolean;
  instruments: boolean;
  detail: boolean;
  runs: boolean;
}

const at = '2026-10-12T16:00:00+05:30';
const endAt = '2026-10-12T16:05:00+05:30';
const severity = (status: string) => ({ pass: 0, warning: 1, fail: 2 })[status] ?? 0;
const scoreOf = (status: string) => ({ pass: 100, warning: 92, fail: 75 })[status] ?? 0;

const rowKeys = {
  symbol: (r: QualityInstrumentRow) => r.symbol,
  name: (r: QualityInstrumentRow) => r.name,
  status: (r: QualityInstrumentRow) => ({ notChecked: 0, pass: 1, warning: 2, fail: 3 })[r.status] ?? 0,
  score: (r: QualityInstrumentRow) => r.score ?? -1,
  coverage: (r: QualityInstrumentRow) => r.coverage ?? -1,
  barsChecked: (r: QualityInstrumentRow) => r.barsChecked ?? -1,
  missingMinutes: (r: QualityInstrumentRow) => r.missingMinutes ?? -1,
  checkedAt: (r: QualityInstrumentRow) => r.checkedAt ?? '',
} as const;

/** The table query over rows in memory: q, status, sort (prefix - for descending), page, pageSize. */
export function searchQuality(items: readonly QualityInstrumentRow[], params: URLSearchParams) {
  const q = params.get('q')?.toLowerCase();
  const status = params.get('status');
  const sort = params.get('sort') ?? 'symbol';
  const descending = sort.startsWith('-');
  const name = descending ? sort.slice(1) : sort;
  const key = name in rowKeys ? rowKeys[name as keyof typeof rowKeys] : rowKeys.symbol;
  const page = Number(params.get('page') ?? 1);
  const pageSize = Number(params.get('pageSize') ?? 50);
  const matches = items
    .filter((r) => !q || r.symbol.toLowerCase().includes(q) || r.name.toLowerCase().includes(q))
    .filter((r) => !status || r.status === status)
    .sort((a, b) => {
      const [x, y] = [key(a), key(b)];
      const order = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y));
      return (descending ? -order : order) || a.symbol.localeCompare(b.symbol);
    });
  return { items: matches.slice((page - 1) * pageSize, page * pageSize), page, pageSize, total: matches.length };
}

/**
 * A stateful stand-in for the Data quality API and for the two data routes the screen watches a job with (the overview
 * and one job). The table, the details (with `differencesOnly` and `timeframe`), the runs, the counts and the gate work
 * over one in-memory state. A job started with Run checks, Re-fetch or Refresh aggregates moves on one step each time the
 * data overview is read; when it ends, a fix action repairs the instrument (unless `fixLeavesIssues`), a run is
 * recorded and the next read of the table shows the new state. `calls` records what was asked.
 */
export function createQualityFake(options: QualityFakeOptions = {}) {
  let rows: QualityInstrumentRow[] = [...(options.rows ?? sampleQualityRows)];
  let runs: QualityRun[] = [...(options.runs ?? [buildQualityRun()])];
  const details = new Map<string, QualityInstrumentDetail>(Object.entries(options.details ?? {}));
  let history: DataJob[] = [];
  let running: DataJob | null = null;
  let cursor = { phase: 0, step: 0 };
  let nextJob = 20;
  let startError = options.startError ?? null;
  const dhan = buildDhanOverview(options.dhan);
  const calls = {
    run: [] as { instrumentId: string | null }[],
    refetch: [] as string[],
    fixAll: [] as { steps: string[]; refetchGaps: boolean }[],
    refresh: [] as string[],
    overview: 0,
    instruments: [] as string[],
    detail: [] as { id: string; query: string }[],
    runs: 0,
  };
  const failures: QualityFailures = { overview: false, instruments: false, detail: false, runs: false };
  const serverError = () => problemResponse({ title: 'The data service is not available.', code: 'data.down' }, 500);

  const detailOf = (row: QualityInstrumentRow) => details.get(row.instrumentId) ?? buildQualityDetail(row);

  const newJob = (steps: readonly PhaseKey[], id: string, instrumentId: string | null): DataJob => {
    const phase = <P extends { status: string }>(key: PhaseKey | 'csv', base: P): P => {
      const on = key !== 'csv' && steps.includes(key);
      const total = instrumentId ? 1 : rows.filter((r) => r.barsChecked !== 0).length;
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
      startedAt: at,
      finishedAt: null,
      steps: [...steps],
      trigger: 'manual',
      refetchGaps: steps.includes('dhan'),
      currentPhase: steps[0] ?? null,
      instrumentId,
      csv: phase('csv', buildCsvPhase()),
      dhan: phase('dhan', buildDhanPhase({ bars: 0, inserted: 0 })),
      aggregate: phase('aggregate', buildAggregatePhase({ buckets: 0 })),
      quality: phase('quality', buildQualityPhase({ passed: 0, warned: 0, failed: 0, runId: null })),
    });
  };

  if (options.running) {
    running = newJob(options.running.steps ?? ['quality'], dataJobId(2), options.running.instrumentId ?? null);
  }

  /** Recomputes a row from the checks of its detail (the status is the worst check, the score follows it). */
  const settleRow = (row: QualityInstrumentRow): QualityInstrumentRow => {
    const checks = buildChecks(row);
    const worst = (['completeness', 'sanity', 'spikes', 'aggregates', 'freshness'] as const)
      .map((k) => checks[k].status)
      .reduce((a, b) => (severity(b) > severity(a) ? b : a), 'pass');
    return {
      ...row,
      status: worst,
      score: scoreOf(worst),
      checkedAt: endAt,
      issues: worst === 'pass' ? [] : row.issues,
    };
  };

  const repair = (row: QualityInstrumentRow, parts: { completeness?: boolean }): QualityInstrumentRow => {
    const fixed: QualityInstrumentRow = {
      ...row,
      checks: row.checks && {
        ...row.checks,
        aggregates: 'pass',
        completeness: parts.completeness ? 'pass' : row.checks.completeness,
        freshness: parts.completeness ? 'pass' : row.checks.freshness,
      },
      aggregates: Object.fromEntries(aggregateTimeframes.map((tf) => [tf, 'pass'])),
      missingMinutes: parts.completeness ? 0 : row.missingMinutes,
      coverage: parts.completeness ? 100 : row.coverage,
    };
    const settled = settleRow(fixed);
    details.set(row.instrumentId, {
      ...buildQualityDetail(settled),
      buckets: {
        ...buildQualityDetail(settled).buckets,
        items: sampleBuckets(false),
        total: sampleBuckets(false).length,
        differences: 0,
      },
    });
    return settled;
  };

  /** What a finished job leaves behind: the rows, the detail of a fixed instrument and a run in the history. */
  const settle = (job: DataJob) => {
    const failed = job.status === 'failed';
    const id = job.instrumentId ?? null;
    if (!failed) {
      if (id) {
        rows = rows.map((row) => {
          if (row.instrumentId !== id) return row;
          if (options.fixLeavesIssues) return { ...row, checkedAt: endAt };
          if (job.steps.includes('dhan')) return repair(row, { completeness: true });
          if (job.steps.includes('aggregate')) return repair(row, {});
          return { ...row, checkedAt: endAt };
        });
      } else if (job.steps.includes('quality')) {
        rows = options.afterRun
          ? [...options.afterRun]
          : rows.map((row) =>
              row.status === 'notChecked'
                ? buildQualityRow({
                    instrumentId: row.instrumentId,
                    symbol: row.symbol,
                    name: row.name,
                    source: row.source,
                    checkedAt: endAt,
                  })
                : row,
            );
      }
    }
    let finished = job;
    if (job.steps.includes('quality')) {
      // The instruments the job checked: the one it is about, else every one that has a result now.
      const checked = rows.filter((r) => (id ? r.instrumentId === id : r.status !== 'notChecked'));
      const counts = (status: string) => checked.filter((r) => r.status === status).length;
      if (!failed && job.quality) {
        finished = {
          ...job,
          quality: { ...job.quality, passed: counts('pass'), warned: counts('warning'), failed: counts('fail') },
        };
      }
      runs = [
        buildQualityRun({
          id: qualityRunId(2 + runs.length),
          trigger: 'manual',
          status: failed ? 'failed' : 'succeeded',
          startedAt: at,
          finishedAt: endAt,
          instruments: id ? 1 : rows.length,
          passed: counts('pass'),
          warned: counts('warning'),
          failed: counts('fail'),
          error: failed ? job.error : null,
          instrumentId: id,
          jobId: job.id,
        }),
        ...runs,
      ];
    }
    history = [finished, ...history];
  };

  const withPhase = (job: DataJob, key: PhaseKey, patch: Partial<QualityPhase>): DataJob => ({
    ...job,
    [key]: { ...job[key], ...patch },
  });

  /** One step: the phase in turn goes from empty to half way to its end, then the next phase starts. */
  const advance = () => {
    if (!running) return;
    const steps = order.filter((key) => running?.steps.includes(key));
    const key = steps[cursor.phase];
    if (!key) return;
    let job = running;
    const total = job[key]?.total ?? 0;
    if (cursor.step === 0) {
      job = withPhase(job, key, {
        status: 'running',
        startedAt: at,
        done: Math.floor(total / 2),
        message: key === 'quality' ? 'Checking NSE:INFY' : `Working on ${key}`,
        ...(key === 'quality' ? { passed: 1, warned: 0, failed: 0 } : {}),
      });
      cursor = { ...cursor, step: 1 };
      running = job;
      return;
    }
    const failed = key === 'quality' && typeof options.failQuality === 'string';
    job = withPhase(job, key, {
      status: failed ? 'failed' : 'succeeded',
      finishedAt: endAt,
      done: failed ? 0 : total,
      message: failed ? options.failQuality : null,
      ...(key === 'quality' && !failed ? { passed: 2, warned: 1, failed: 1 } : {}),
    });
    const next = steps[cursor.phase + 1];
    if (failed || !next) {
      running = null;
      settle({
        ...job,
        status: failed ? 'failed' : 'succeeded',
        currentPhase: null,
        finishedAt: endAt,
        error: failed ? (options.failQuality ?? null) : null,
      });
      return;
    }
    cursor = { phase: cursor.phase + 1, step: 0 };
    running = withPhase({ ...job, currentPhase: next }, next, { status: 'running' });
  };

  const counts = () => {
    const tracked = rows.length;
    const count = (status: string) => rows.filter((r) => r.status === status).length;
    const scored = rows.filter((r) => r.score !== null);
    return {
      tracked,
      checked: scored.length,
      passed: count('pass'),
      warned: count('warning'),
      failed: count('fail'),
      notChecked: count('notChecked'),
      score: scored.length > 0 ? scored.reduce((sum, r) => sum + (r.score ?? 0), 0) / scored.length : null,
    };
  };

  /** The gate follows the rows: blocked before the first run and above the share of failing instruments. */
  const gate = () => {
    const c = counts();
    const max = options.maxFailedPercent ?? 5;
    const lastFull = runs.find((r) => r.instrumentId === null) ?? null;
    const failedPercent = c.checked > 0 ? (c.failed / c.checked) * 100 : 0;
    const reasons = [
      ...(lastFull === null ? ['No checks have run yet.'] : []),
      ...(lastFull?.status === 'failed' ? ['The last run failed.'] : []),
      ...(failedPercent > max
        ? [`${c.failed} of ${c.checked} checked instruments fail (${failedPercent.toFixed(1)} %), above ${max} %.`]
        : []),
      ...(failedPercent <= max && c.failed > 0
        ? [`${c.failed} instruments fail and are skipped by the nightly scan.`]
        : []),
    ];
    const blocked = lastFull === null || lastFull.status === 'failed' || failedPercent > max;
    return buildQualityGate({
      ok: !blocked && c.failed === 0,
      blocked,
      reasons,
      checkedInstruments: c.checked,
      failedInstruments: c.failed,
      failedPercent,
      maxFailedPercent: max,
      lastRunAt: lastFull?.finishedAt ?? null,
    });
  };

  const start = (steps: readonly PhaseKey[], instrumentId: string | null) => {
    const error = startError;
    startError = null;
    if (instrumentId && !rows.some((r) => r.instrumentId === instrumentId)) {
      return problemResponse({ title: 'The instrument does not exist.', code: 'data.instrument_not_found' }, 404);
    }
    if (running || error === 'job_running') {
      return problemResponse({ title: 'An update is already running.', code: 'data.job_running' }, 409);
    }
    if (error === 'dhan_not_available') {
      return problemResponse(
        { title: 'Dhan cannot be asked for this instrument.', code: 'data.dhan_not_available' },
        409,
      );
    }
    if (error === 'dhan_not_connected') {
      return problemResponse(
        { title: 'Dhan is not connected for the Data account.', code: 'data.dhan_not_connected' },
        409,
      );
    }
    if (error === 'not_found') {
      return problemResponse({ title: 'The instrument does not exist.', code: 'data.instrument_not_found' }, 404);
    }
    if (error === 'server') return serverError();
    running = newJob(steps, dataJobId(nextJob++), instrumentId);
    cursor = { phase: 0, step: 0 };
    return HttpResponse.json(running, { status: 202 });
  };

  const handlers = [
    http.get('/api/data/overview', () => {
      calls.overview += 1;
      if (options.autoAdvance !== false) advance();
      return HttpResponse.json(
        buildDataOverview({
          dhan,
          runningJob: running,
          gaps: { open: options.openGaps ?? 0, unfillable: 0, filled: 0, lastDetectedAt: null },
        }),
      );
    }),
    http.get('/api/data/jobs/:id', ({ params }) => {
      const job = [...(running ? [running] : []), ...history].find((j) => j.id === params.id);
      return job
        ? HttpResponse.json(job)
        : problemResponse({ title: 'The job does not exist.', code: 'data.job_not_found' }, 404);
    }),
    http.get('/api/data/quality/overview', () => {
      if (failures.overview) return serverError();
      const lastRun = runs.find((r) => r.instrumentId === null) ?? null;
      const body: QualityOverview = {
        lastRun,
        counts: counts(),
        gate: gate(),
        window: { sessions: 5, from: '2026-10-06', to: '2026-10-12' },
        spikeMultiple: 20,
        maxMissingPercent: 1,
        pendingCorporateActions: options.pendingCorporateActions ?? 0,
      };
      return HttpResponse.json(body);
    }),
    http.get('/api/data/quality/gate', () => HttpResponse.json(gate())),
    http.get('/api/data/quality/instruments', ({ request }) => {
      const url = new URL(request.url);
      calls.instruments.push(url.search);
      if (failures.instruments) return serverError();
      return HttpResponse.json(searchQuality(rows, url.searchParams));
    }),
    http.get('/api/data/quality/instruments/:id', ({ params, request }) => {
      const url = new URL(request.url);
      calls.detail.push({ id: String(params.id), query: url.search });
      if (failures.detail) return serverError();
      const row = rows.find((r) => r.instrumentId === params.id);
      if (!row)
        return problemResponse({ title: 'The instrument does not exist.', code: 'data.instrument_not_found' }, 404);
      const detail = detailOf(row);
      const timeframe = url.searchParams.get('timeframe');
      const differencesOnly = url.searchParams.get('differencesOnly') === 'true';
      const inFrame = detail.buckets.items.filter((b) => !timeframe || b.timeframe === timeframe);
      return HttpResponse.json({
        ...detail,
        buckets: {
          ...detail.buckets,
          total: inFrame.length,
          differences: inFrame.filter((b) => b.differs).length,
          items: differencesOnly ? inFrame.filter((b) => b.differs) : inFrame,
        },
      });
    }),
    http.get('/api/data/quality/runs', ({ request }) => {
      calls.runs += 1;
      if (failures.runs) return serverError();
      const take = Number(new URL(request.url).searchParams.get('take') ?? 20);
      return HttpResponse.json(runs.slice(0, take));
    }),
    http.post('/api/data/quality/run', async ({ request }) => {
      const body = (await request.json().catch(() => null)) as { instrumentId?: string | null } | null;
      calls.run.push({ instrumentId: body?.instrumentId ?? null });
      return start(['quality'], body?.instrumentId ?? null);
    }),
    http.post('/api/data/refresh', async ({ request }) => {
      const body = (await request.json().catch(() => null)) as { steps?: PhaseKey[]; refetchGaps?: boolean } | null;
      calls.fixAll.push({ steps: body?.steps ?? [], refetchGaps: body?.refetchGaps ?? false });
      return start(body?.steps ?? ['quality'], null);
    }),
    http.post('/api/data/quality/instruments/:id/refetch', ({ params }) => {
      calls.refetch.push(String(params.id));
      return start(['dhan', 'aggregate', 'quality'], String(params.id));
    }),
    http.post('/api/data/quality/instruments/:id/refresh-aggregates', ({ params }) => {
      calls.refresh.push(String(params.id));
      return start(['aggregate', 'quality'], String(params.id));
    }),
  ];

  return {
    handlers,
    calls,
    failures,
    /** Moves the job in progress one step on (for tests that look at each step). */
    advance,
    /** A run starts by itself (the scheduled one): the next read of the overview carries it. */
    startAutomatic: () => {
      running = { ...newJob(['quality'], dataJobId(nextJob++), null), trigger: 'daily', requestedBy: 'system' };
      cursor = { phase: 0, step: 0 };
    },
    get running() {
      return running;
    },
    /** The rows as the next read would answer them. */
    get rows() {
      return [...rows];
    },
    get runs() {
      return [...runs];
    },
  };
}

export type QualityFake = ReturnType<typeof createQualityFake>;
