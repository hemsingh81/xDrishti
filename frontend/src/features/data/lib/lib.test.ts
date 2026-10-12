import { ApiError } from '@/shared/api';
import {
  buildCsvPhase,
  buildDataJob,
  buildDhanPhase,
  buildAggregatePhase,
  buildDataOverview,
  buildRunningJob,
} from '@/test/fixtures';
import { isJobRunning, phaseReason, startErrorText } from './errors';
import { emptyFilters, formatSort, parseFilters, toParams } from './filters';
import { outcomeText, rowStatusText, sourceLabel } from './labels';
import { currentPhase, percentDone, stepsText } from './phases';
import { aggregateResult, csvResult, dhanResult, jobCounts, repairLine } from './summaries';

describe('filters', () => {
  it('reads the table state from the URL and falls back to the defaults for anything unknown', () => {
    expect(parseFilters(new URLSearchParams('q=tcs&status=behind&sort=-bars&page=3&size=50'))).toEqual({
      q: 'tcs',
      status: 'behind',
      sort: { field: 'bars', descending: true },
      page: 3,
      pageSize: 50,
    });
    expect(parseFilters(new URLSearchParams('status=odd&sort=colour&page=0&size=7'))).toEqual(emptyFilters);
  });

  it('writes only what differs from the defaults, and keeps parameters that are not its own', () => {
    expect(toParams(emptyFilters).toString()).toBe('');
    const next = toParams(
      {
        ...emptyFilters,
        q: ' tcs ',
        status: 'empty',
        sort: { field: 'lastBar', descending: false },
        page: 2,
        pageSize: 100,
      },
      new URLSearchParams('id=abc'),
    );
    expect(next.toString()).toBe('id=abc&q=tcs&status=empty&sort=lastBar&page=2&size=100');
    expect(formatSort({ field: 'name', descending: true })).toBe('-name');
  });
});

describe('labels', () => {
  it('words the status of a row', () => {
    expect(rowStatusText({ status: 'current', daysBehind: 0 })).toBe('Current');
    expect(rowStatusText({ status: 'behind', daysBehind: 3 })).toBe('Behind 3 d');
    expect(rowStatusText({ status: 'behind', daysBehind: null })).toBe('Behind');
    expect(rowStatusText({ status: 'empty', daysBehind: null })).toBe('Empty');
  });

  it('names the source of the bars', () => {
    expect([sourceLabel('csv'), sourceLabel('dhan'), sourceLabel('mixed'), sourceLabel(null)]).toEqual([
      'CSV',
      'Dhan',
      'CSV + Dhan',
      '—',
    ]);
  });

  it('tells how a job ended', () => {
    expect(outcomeText(buildDataJob())).toBe(
      'Data updated: 214 files loaded · 4.1Cr rows · 214 instruments · 4,500 bars added · 1.2Cr bars written',
    );
    expect(outcomeText(buildDataJob({ status: 'failed', error: 'Boom.' }))).toBe('The update failed: Boom.');
    expect(outcomeText(buildDataJob({ status: 'failed', error: null }))).toBe(
      'The update failed: see Recent runs for the reason.',
    );
  });
});

describe('summaries', () => {
  it('summarises each phase', () => {
    expect(csvResult(buildCsvPhase({ filesLoaded: 1, filesUnchanged: 3, rows: 1_500 }))).toBe(
      '1 file loaded · 3 unchanged · 1.5K rows',
    );
    expect(repairLine(buildCsvPhase({ repaired: 1, rejected: 2 }))).toBe('1 repaired, 2 rejected');
    expect(dhanResult(buildDhanPhase({ instrumentsFetched: 1, inserted: 1, updated: 0 }))).toBe(
      '1 instrument · 1 bar added',
    );
    expect(aggregateResult(buildAggregatePhase({ full: true, buckets: 2_000 }))).toBe('2K bars written (full rebuild)');
  });

  it('counts only the phases that succeeded', () => {
    const job = buildDataJob({
      dhan: buildDhanPhase({ status: 'skipped' }),
      aggregate: buildAggregatePhase({ status: 'failed' }),
    });
    expect(jobCounts(job)).toEqual(['214 files loaded · 4.1Cr rows']);
  });
});

describe('phases', () => {
  it('shows the running job’s phase when the job asked for it, else the last finished one', () => {
    const running = buildRunningJob();
    const overview = buildDataOverview({ runningJob: running });
    expect(currentPhase(overview, 'csv')).toBe(running.csv);

    const dhanOnly = buildRunningJob({
      dhan: { ...buildDhanPhase(), status: 'running' },
      csv: { ...buildCsvPhase(), status: 'notRequested' },
    });
    const second = buildDataOverview({ runningJob: dhanOnly });
    expect(currentPhase(second, 'csv')).toBe(second.lastJobs.csv?.csv);
    expect(currentPhase(second, 'dhan')).toBe(dhanOnly.dhan);

    expect(currentPhase(buildDataOverview({ lastJobs: { csv: null, dhan: null, aggregate: null } }), 'csv')).toBeNull();
  });

  it('computes progress and names the steps of a run', () => {
    expect(percentDone(107, 214)).toBe(50);
    expect(percentDone(5, 0)).toBe(0);
    expect(percentDone(9, 3)).toBe(100);
    expect(stepsText(['aggregate', 'csv', 'dhan'])).toBe('All steps');
    expect(stepsText(['dhan', 'csv'])).toBe('CSV · Dhan');
  });
});

describe('errors', () => {
  const problem = (code: string, status = 409, title = 'Title') => new ApiError(status, { title, status, code });

  it('recognises a job that is already running', () => {
    expect(isJobRunning(problem('data.job_running'))).toBe(true);
    expect(isJobRunning(problem('data.master_missing'))).toBe(false);
    expect(isJobRunning(new Error('x'))).toBe(false);
  });

  it('words the problems of starting a job', () => {
    expect(startErrorText(problem('data.job_running'))).toBe('An update is already running.');
    expect(startErrorText(problem('data.master_missing'))).toMatch(/instrument list is missing/);
    expect(startErrorText(problem('data.other', 500, 'Boom'))).toBe('Boom');
    expect(startErrorText(new Error('offline'))).toMatch(/Could not reach the server/);
  });

  it('words why a step did not finish', () => {
    expect(phaseReason('Because.', 'data.x')).toBe('Because.');
    expect(phaseReason(null, 'data.dhan_not_connected')).toMatch(/Reconnect it in Accounts/);
    expect(phaseReason(null, 'data.unknown')).toBe('The step did not finish.');
    expect(phaseReason(null, null)).toBe('The step did not finish.');
  });
});
