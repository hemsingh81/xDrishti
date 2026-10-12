import { http, HttpResponse } from 'msw';
import type {
  AggregatePhase,
  CalendarDay,
  CalendarDayInput,
  CalendarRefreshResult,
  CorporateAction,
  CsvPhase,
  DataCoverageRow,
  DataJob,
  DataNotice,
  DataOverview,
  GapRow,
  DhanPhase,
  StartDataJobInput,
} from '@/shared/api';
import {
  buildAggregatePhase,
  buildCsvPhase,
  buildDataJob,
  buildDataOverview,
  buildDhanPhase,
  buildCalendarDay,
  buildInstrumentBars,
  dataJobId,
  sampleCalendarDays,
  sampleCalendarProposals,
  sampleCoverage,
  searchCoverage,
  searchGaps,
} from './fixtures';
import { createActionsFake } from './actionsFake';
import { createCsvFake, type CsvFakeOptions } from './csvFake';
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
  /** The calendar the page opens with: confirmed days and proposals (default: the samples). */
  calendar?: { days?: readonly CalendarDay[]; proposals?: readonly CalendarDay[] };
  /** What a refresh from NSE does: `ok` adds one NSE holiday (the `nseHoliday` below), `refused` answers 200 with `succeeded: false`. */
  nseRefresh?: 'ok' | 'refused';
  /** What a refresh from NSE does: `ok` adds the NSE holiday below, `refused` answers 200 with `succeeded: false`. */
  /** The gap rows `GET /api/data/gaps` lists, newest first (default: none). The overview's `gaps` counts follow them. */
  gaps?: readonly GapRow[];
  /** What `GET /api/data/notices` answers (default: none). */
  notices?: readonly DataNotice[];
  /** The corporate actions the page opens with (default: none); the overview's pending count follows them. */
  corporateActions?: readonly CorporateAction[];
  /** What "Detect now" finds (suggestions, unless an action for the instrument and ex-date exists). */
  detectFinds?: readonly CorporateAction[];
  /** The CSV folder's files and the preview answers (default: a mixed sample). */
  csv?: CsvFakeOptions;
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
  calendar: boolean;
  gaps: boolean;
  notices: boolean;
}

/** The holiday a successful refresh from NSE brings in. */
export const nseHoliday = (): CalendarDay =>
  buildCalendarDay({ date: '2026-11-24', source: 'nse', note: 'Guru Nanak Jayanti' });

/** The fake's "today" for the calendar (the date `nextHoliday` counts from). */
const calendarToday = '2026-10-12';

const isWeekend = (date: string) => [0, 6].includes(new Date(`${date}T00:00:00Z`).getUTCDay());
const byDate = (a: CalendarDay, b: CalendarDay) => a.date.localeCompare(b.date);

const unitsOf = (overview: DataOverview, key: PhaseKey, job?: Pick<DataJob, 'csvFiles' | 'undoCsvFile'>) => {
  if (key !== 'csv') return 5;
  if (job?.undoCsvFile) return 1;
  return job?.csvFiles?.length ?? overview.csv.filesTotal;
};

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
    /** File names of each undo request. */
    undo: [] as string[],
    coverage: [] as string[],
    bars: [] as { id: string; timeframe: string; take: string | null }[],
    /** The `adjusted` parameter of each bars read ('true', 'false' or null when left out). */
    barsAdjusted: [] as (string | null)[],
    /** Coverage reads that only look something up (the CSV end, the instrument type-ahead): not table pages. */
    lookups: [] as string[],
    overview: 0,
    /** Bodies of POST /api/data/calendar, dates of confirm / remove, and the count of refreshes from NSE. */
    calendarAdd: [] as CalendarDayInput[],
    calendarConfirm: [] as string[],
    calendarRemove: [] as string[],
    calendarRefresh: 0,
    /** Query strings of GET /api/data/gaps. */
    gaps: [] as string[],
    notices: 0,
  };
  const failures: DataFailures = {
    overview: false,
    coverage: false,
    bars: false,
    jobs: false,
    calendar: false,
    gaps: false,
    notices: false,
  };
  const csv = createCsvFake(options.csv);
  const actions = createActionsFake({ actions: options.corporateActions, found: options.detectFinds }, () => coverage);
  const gapRows = options.gaps;
  const gapCounts = (rows: readonly GapRow[]) => ({
    open: rows.filter((g) => g.status === 'open').length,
    unfillable: rows.filter((g) => g.status === 'unfillable').length,
    filled: rows.filter((g) => g.status === 'filled').length,
    lastDetectedAt: rows.map((g) => g.detectedAt).sort((a, b) => b.localeCompare(a))[0] ?? null,
  });
  let days: CalendarDay[] = [...(options.calendar?.days ?? sampleCalendarDays)];
  let proposals: CalendarDay[] = [...(options.calendar?.proposals ?? sampleCalendarProposals)];
  const nextHoliday = () => [...days].sort(byDate).find((d) => d.kind === 'holiday' && d.date >= calendarToday) ?? null;
  const notFound = () =>
    problemResponse({ title: 'That day is not in the calendar.', code: 'data.calendar_day_not_found' }, 404);
  const invalid = (errors: Record<string, string[]>) =>
    problemResponse({ title: 'The calendar day is not valid.', code: 'data.invalid', errors }, 400);
  const serverError = () => problemResponse({ title: 'The data service is not available.', code: 'data.down' }, 500);

  const newJob = (
    body: StartDataJobInput | null,
    id: string,
    extra: Pick<DataJob, 'undoCsvFile' | 'instrumentId'> = {},
  ): DataJob => {
    const steps = order.filter((key) => (body?.steps ?? order).includes(key));
    const phase = <P extends { status: string }>(key: PhaseKey, base: P): P => {
      const on = steps.includes(key);
      const total = unitsOf(overview, key, { csvFiles: body?.csvFiles ?? null, undoCsvFile: extra.undoCsvFile });
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
      refetchGaps: body?.refetchGaps ?? false,
      csvFiles: body?.csvFiles ?? null,
      ...extra,
      trigger: 'manual',
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
    const loadedNow = job.csvFiles ?? csv.files.filter((f) => f.mapping === 'mapped').map((f) => f.name);
    if (done('csv') && job.undoCsvFile) csv.markPending(job.undoCsvFile);
    else if (done('csv')) csv.markLoaded(loadedNow);
    const undone = job.undoCsvFile && done('csv');
    overview = {
      ...overview,
      ...options.afterRun?.overview,
      csv: undone
        ? { ...overview.csv, filesLoaded: overview.csv.filesLoaded - 1, filesPending: overview.csv.filesPending + 1 }
        : done('csv')
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
      ...(key === 'csv' && !failed && job.undoCsvFile ? undoResult(job.undoCsvFile) : {}),
    });
    if (key === 'csv' && !failed && job.undoCsvFile) {
      const overlaps = csv.find(job.undoCsvFile)?.overlapsWith ?? [];
      if (overlaps.length > 0) {
        job = { ...job, warnings: [`The undo also removed CSV bars loaded from: ${overlaps.join(', ')}.`] };
      }
    }
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

  /** The csv block of an undo: the bars it removed (the file's rows) and its message. */
  const undoResult = (name: string): Partial<CsvPhase> => {
    const removed = csv.find(name)?.rows ?? 0;
    return {
      barsRemoved: removed,
      message: `CSV load of ${name} undone, ${removed.toLocaleString('en-US')} bars removed`,
      filesLoaded: 0,
      rows: 0,
      inserted: 0,
    };
  };

  const handlers = [
    ...csv.handlers,
    http.get('/api/data/overview', () => {
      calls.overview += 1;
      if (failures.overview) return serverError();
      if (options.autoAdvance !== false) advance();
      return HttpResponse.json({
        ...overview,
        runningJob: running,
        nextHoliday: nextHoliday(),
        calendarProposals: proposals.length,
        pendingCorporateActions: actions.pending,
        ...(gapRows ? { gaps: gapCounts(gapRows) } : {}),
      });
    }),
    http.get('/api/data/gaps', ({ request }) => {
      const url = new URL(request.url);
      calls.gaps.push(url.search);
      if (failures.gaps) return serverError();
      return HttpResponse.json(searchGaps(gapRows ?? [], url.searchParams));
    }),
    http.get('/api/data/notices', () => {
      calls.notices += 1;
      if (failures.notices) return serverError();
      return HttpResponse.json({ items: options.notices ?? [] });
    }),
    http.get('/api/data/instruments', ({ request }) => {
      const url = new URL(request.url);
      // The type-ahead of the dialogs reads 20 at a time; any other size is the table (100 in the app, a few in tests).
      const lookup = url.searchParams.get('pageSize') === '20';
      (lookup ? calls.lookups : calls.coverage).push(url.search);
      if (failures.coverage) return serverError();
      return HttpResponse.json(searchCoverage(coverage, url.searchParams));
    }),
    http.get('/api/data/instruments/:id/bars', ({ params, request }) => {
      const url = new URL(request.url);
      const timeframe = url.searchParams.get('timeframe') ?? '1m';
      calls.bars.push({ id: String(params.id), timeframe, take: url.searchParams.get('take') });
      calls.barsAdjusted.push(url.searchParams.get('adjusted'));
      if (failures.bars) return serverError();
      const row = coverage.find((r) => r.id === params.id);
      if (!row)
        return problemResponse({ title: 'The instrument does not exist.', code: 'data.instrument_not_found' }, 404);
      const bars = {
        ...buildInstrumentBars(row.symbol, timeframe),
        instrumentId: row.id,
        name: row.name,
        // The sample has no weekly or monthly bars yet, to show the empty state.
        ...(timeframe === '1M' ? { items: [] } : {}),
      };
      return HttpResponse.json(url.searchParams.get('adjusted') === 'true' ? actions.adjust(bars) : bars);
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
      if (body?.csvFiles) {
        const unknown = body.csvFiles.filter((name) => !csv.has(name));
        if (body.csvFiles.length === 0 || unknown.length > 0) {
          return problemResponse(
            {
              title: 'The CSV files are not valid.',
              code: 'data.invalid',
              errors: {
                csvFiles: [
                  body.csvFiles.length === 0 ? 'Name at least one file.' : `Not in the folder: ${unknown.join(', ')}.`,
                ],
              },
            },
            400,
          );
        }
      }
      running = newJob(body, dataJobId(nextJob++));
      cursor = { phase: 0, step: 0 };
      return HttpResponse.json(running, { status: 202 });
    }),
    http.post('/api/data/csv/files/:name/undo', ({ params }) => {
      const name = decodeURIComponent(String(params.name));
      calls.undo.push(name);
      const file = csv.find(name);
      if (!file || file.state === 'pending') {
        return problemResponse({ title: 'That file was never loaded.', code: 'data.csv_file_not_found' }, 404);
      }
      if (running) return problemResponse({ title: 'An update is already running.', code: 'data.job_running' }, 409);
      running = newJob({ steps: ['csv', 'aggregate'] }, dataJobId(nextJob++), {
        undoCsvFile: name,
        instrumentId: file.instrument?.id ?? null,
      });
      cursor = { phase: 0, step: 0 };
      return HttpResponse.json(running, { status: 202 });
    }),
    http.get('/api/data/calendar', () => {
      if (failures.calendar) return serverError();
      return HttpResponse.json({
        from: calendarToday,
        to: '2027-10-12',
        days: [...days].sort(byDate),
        proposals: [...proposals].sort(byDate),
        nextHoliday: nextHoliday(),
      });
    }),
    http.post('/api/data/calendar', async ({ request }) => {
      const body = (await request.json()) as CalendarDayInput;
      calls.calendarAdd.push(body);
      if (failures.calendar) return serverError();
      const errors: Record<string, string[]> = {};
      if (!body.date) errors.date = ['Enter a date.'];
      else if (body.kind === 'holiday' && isWeekend(body.date)) {
        errors.date = ['A weekend is not a trading day, so it cannot be a holiday.'];
      }
      if (body.kind !== 'holiday' && body.kind !== 'special') errors.kind = ['Choose holiday or special.'];
      if (body.kind === 'special' && (body.sessionFrom ?? '09:15') >= (body.sessionTo ?? '15:30')) {
        errors.sessionTo = ['The session must end after it starts.'];
      }
      if ((body.note ?? '').length > 200) errors.note = ['The note can have at most 200 characters.'];
      if (Object.keys(errors).length > 0) return invalid(errors);
      const special = body.kind === 'special';
      const from = body.sessionFrom ?? '09:15';
      const to = body.sessionTo ?? '15:30';
      const minutes = (t: string) => Number(t.slice(0, 2)) * 60 + Number(t.slice(3, 5));
      const day = buildCalendarDay({
        date: body.date ?? '',
        kind: body.kind ?? 'holiday',
        sessionFrom: special ? from : null,
        sessionTo: special ? to : null,
        minutes: special ? minutes(to) - minutes(from) : null,
        source: 'manual',
        status: 'confirmed',
        note: body.note ?? null,
      });
      const replaced = days.some((d) => d.date === day.date) || proposals.some((d) => d.date === day.date);
      days = [...days.filter((d) => d.date !== day.date), day];
      proposals = proposals.filter((d) => d.date !== day.date);
      return HttpResponse.json(day, { status: replaced ? 200 : 201 });
    }),
    // Before the :date routes, which would take "refresh" for a date.
    http.post('/api/data/calendar/refresh', () => {
      calls.calendarRefresh += 1;
      const at = '2026-10-12T16:20:00+05:30';
      if (options.nseRefresh === 'refused') {
        const refused: CalendarRefreshResult = {
          succeeded: false,
          message: 'NSE did not answer (HTTP 403); nothing changed.',
          added: 0,
          updated: 0,
          unchanged: 0,
          skipped: 0,
          at,
        };
        return HttpResponse.json(refused);
      }
      const added = nseHoliday();
      days = [...days.filter((d) => d.date !== added.date), added];
      const done: CalendarRefreshResult = {
        succeeded: true,
        message: 'NSE holidays refreshed: 1 added, 0 updated, 2 unchanged.',
        added: 1,
        updated: 0,
        unchanged: 2,
        skipped: 0,
        at,
      };
      return HttpResponse.json(done);
    }),
    http.put('/api/data/calendar/:date/confirm', ({ params }) => {
      const date = String(params.date);
      calls.calendarConfirm.push(date);
      const found = proposals.find((d) => d.date === date);
      if (!found) return notFound();
      // A proposal next to a confirmed day replaces it.
      const confirmed = { ...found, status: 'confirmed', replaces: null };
      proposals = proposals.filter((d) => d.date !== date);
      days = [...days.filter((d) => d.date !== date), confirmed];
      return HttpResponse.json(confirmed);
    }),
    http.delete('/api/data/calendar/:date', ({ params }) => {
      const date = String(params.date);
      calls.calendarRemove.push(date);
      if (![...days, ...proposals].some((d) => d.date === date)) return notFound();
      days = days.filter((d) => d.date !== date);
      proposals = proposals.filter((d) => d.date !== date);
      return new HttpResponse(null, { status: 204 });
    }),
    ...actions.handlers,
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
    /** The CSV folder: `files`, `calls` (listing and preview requests) and `failures`. */
    csv,
    /** Moves the job in progress one step on (for tests that look at each step). */
    advance,
    /** The scheduled run starts by itself: the next read of the overview carries it. */
    startAutomatic: () => {
      running = { ...newJob(null, dataJobId(nextJob++)), trigger: 'daily', requestedBy: 'system' };
      cursor = { phase: 0, step: 0 };
    },
    /** Adds proposals the stored data now contradicts the calendar with (a run found them). */
    addProposals: (...found: CalendarDay[]) => {
      proposals = [...proposals, ...found];
    },
    failures,
    /** The corporate actions: `calls`, `failures.list`, `items` and `pending`. */
    actions,
    /** The job in progress, if any. */
    get running() {
      return running;
    },
    /** The calendar as the next read would answer it. */
    get calendar() {
      return { days: [...days], proposals: [...proposals] };
    },
    /** The overview as the next read would answer it (without moving the job on). */
    get overview() {
      return overview;
    },
  };
}

export type DataFake = ReturnType<typeof createDataFake>;
