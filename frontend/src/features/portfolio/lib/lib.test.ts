import { ApiError } from '@/shared/api';
import { buildReturns, buildSyncRun } from '@/test/fixtures';
import { accountTabPath, confirmationOf, fieldMessages, problemKind, problemText } from './errors';
import { directionOf, signedInr, signedPercent, xirrOf } from './figures';
import { activeFilterCount, emptyFilters, formatSort, parseFilters, parseSort, toParams } from './filters';
import { describeSchedule, runCounts, summariseRun } from './syncText';

const parse = (search: string) => parseFilters(new URLSearchParams(search));

describe('filters', () => {
  it('reads the defaults from an empty URL', () => {
    expect(parse('')).toEqual(emptyFilters);
  });

  it('reads every part of the view and ignores what it does not know', () => {
    expect(parse('account=a1&bucket=b1&status=all&q=tcs&sort=-pnl&page=3&size=100')).toEqual({
      account: 'a1',
      bucket: 'b1',
      status: 'all',
      q: 'tcs',
      sort: { field: 'pnl', descending: true },
      page: 3,
      pageSize: 100,
    });
    expect(parse('status=weird&sort=nonsense&page=-2&size=7')).toEqual(emptyFilters);
  });

  it('writes only what differs from the defaults and keeps other parameters', () => {
    expect(toParams(emptyFilters).toString()).toBe('');
    const next = toParams(
      {
        ...emptyFilters,
        status: 'closed',
        q: ' tcs ',
        sort: { field: 'xirr', descending: true },
        page: 2,
        pageSize: 25,
      },
      new URLSearchParams('holding=h1'),
    );
    expect(next.toString()).toBe('holding=h1&status=closed&q=tcs&sort=-xirr&page=2&size=25');
  });

  it('formats and reads a sort', () => {
    expect(formatSort({ field: 'acquiredOn', descending: false })).toBe('acquiredOn');
    expect(parseSort('-bucket')).toEqual({ field: 'bucket', descending: true });
  });

  it('counts the narrowing filters besides the account', () => {
    expect(activeFilterCount(emptyFilters)).toBe(0);
    expect(activeFilterCount({ ...emptyFilters, account: 'a', bucket: 'b', q: 'x', status: 'all' })).toBe(3);
  });
});

describe('figures', () => {
  it('signs and rounds amounts and shows a rounding to zero as flat', () => {
    expect(signedInr(1_23_456)).toBe('+₹1,23,456');
    expect(signedInr(-4_000)).toBe('−₹4,000');
    expect(signedInr(0.4)).toBe('₹0');
    expect(signedInr(-0.4)).toBe('₹0');
    expect(directionOf(-0.4)).toBe('flat');
    expect(directionOf(-0.6)).toBe('down');
  });

  it('signs percentages with two decimals', () => {
    expect(signedPercent(16.1)).toBe('+16.10%');
    expect(signedPercent(-3.2)).toBe('−3.20%');
    expect(signedPercent(0.001)).toBe('0.00%');
    expect(signedPercent(1_234.5)).toBe('+1,234.50%');
  });

  it('gives the XIRR when ok and its own reason for every other state', () => {
    expect(xirrOf(buildReturns({ xirr: 14.2 }))).toEqual({ value: 14.2 });
    const reasons = ['insufficient', 'n_m', 'no_data', 'no_price'].map((state) => {
      const figure = xirrOf(buildReturns({ xirr: null, xirrState: state }));
      expect(figure).toHaveProperty('reason');
      return 'reason' in figure ? figure.reason : '';
    });
    expect(new Set(reasons).size).toBe(4);
    expect(xirrOf(buildReturns({ xirr: null, xirrState: 'insufficient' }))).toMatchObject({ label: '—' });
    expect(xirrOf(buildReturns({ xirr: null, xirrState: 'no_price' }))).toMatchObject({
      label: '—',
      short: 'No price yet',
    });
    expect(xirrOf(buildReturns({ xirr: null, xirrState: 'n_m' }))).toMatchObject({ label: 'n/m' });
    expect(xirrOf(buildReturns({ xirr: null, xirrState: 'ok' }))).toEqual({
      reason: 'XIRR is not available.',
      short: 'Not available',
      label: '—',
    });
    expect(xirrOf(buildReturns({ xirr: null, xirrState: 'something new' }))).toHaveProperty('short', 'Not available');
  });
});

describe('errors', () => {
  const api = (code: string | undefined, extra: object = {}) => new ApiError(409, { title: 'T', code, ...extra });

  it('reads the kind of problem from its code', () => {
    expect(problemKind(api('portfolio.not_connected'))).toBe('not_connected');
    expect(problemKind(api('portfolio.sync_running'))).toBe('sync_running');
    expect(problemKind(api('accounts.broker_error'))).toBe('broker_error');
    expect(problemKind(api('portfolio.conflict'))).toBe('conflict');
    expect(problemKind(api('portfolio.confirmation_stale'))).toBe('confirmation_stale');
    expect(problemKind(api(undefined))).toBe('other');
    expect(problemKind(new Error('offline'))).toBe('network');
  });

  it('words the known problems and falls back to the API title', () => {
    expect(problemText(api('portfolio.role_off'), 'x')).toMatch(/Portfolio role/);
    expect(problemText(api('portfolio.invalid'), 'x')).toBe('T');
    expect(problemText(new ApiError(500), 'Fallback')).toBe('Fallback');
    expect(problemText(new Error('offline'), 'Fallback')).toMatch(/Could not reach the server/);
  });

  it('reads the numbers of the safeguard, and the fresh ones of a stale confirmation', () => {
    const required = api('portfolio.confirmation_required', { listedHoldings: 2, openHoldings: 6, wouldClose: 4 });
    expect(confirmationOf(required)).toEqual({ listedHoldings: 2, openHoldings: 6, wouldClose: 4, stale: false });
    const stale = api('portfolio.confirmation_stale', { listedHoldings: 0, openHoldings: 6, wouldClose: 6 });
    expect(confirmationOf(stale)).toEqual({ listedHoldings: 0, openHoldings: 6, wouldClose: 6, stale: true });
    expect(confirmationOf(api('portfolio.confirmation_required'))).toBeNull();
    expect(confirmationOf(api('portfolio.invalid'))).toBeNull();
  });

  it('joins the messages of a field and points at the account’s tabs', () => {
    expect(fieldMessages(api('portfolio.invalid', { errors: { acquiredOn: ['a', 'b'] } }))).toEqual({
      acquiredOn: 'a b',
    });
    expect(fieldMessages(new Error('x'))).toEqual({});
    expect(accountTabPath('id1', 'connection')).toBe('/accounts?account=id1&tab=connection');
  });
});

describe('sync text', () => {
  it('lists the changes of a run and leaves zero counts out', () => {
    expect(runCounts(buildSyncRun({ newCount: 2, increasedCount: 1, closedCount: 0, unchangedCount: 5 }))).toBe(
      '2 new, 1 increased',
    );
    expect(runCounts(buildSyncRun({ newCount: 0, closedCount: 0, unchangedCount: 5 }))).toBe('no changes');
    expect(runCounts(buildSyncRun({ newCount: 0, closedCount: 0, unchangedCount: 0 }))).toBe('nothing to change');
    expect(runCounts(buildSyncRun({ trigger: 'import', newCount: 12 }))).toBe('12 imported');
  });

  it('summarises each kind of run', () => {
    expect(summariseRun(buildSyncRun({ trigger: 'auto' })).title).toBe('Auto-synced');
    expect(summariseRun(buildSyncRun({ trigger: 'import' })).title).toBe('Imported');
    expect(summariseRun(buildSyncRun({ status: 'failed', error: 'Boom.' }))).toMatchObject({
      tone: 'error',
      detail: 'Boom.',
    });
    expect(summariseRun(buildSyncRun({ status: 'failed', error: null })).detail).toBe('Nothing was changed.');
    expect(summariseRun(buildSyncRun({ status: 'skipped', error: 'Not connected.' }))).toMatchObject({
      tone: 'warning',
      title: 'Skipped',
      detail: 'Not connected.',
    });
    expect(summariseRun(buildSyncRun({ status: 'running' })).tone).toBe('info');
  });

  it('describes the schedule', () => {
    expect(describeSchedule({ mode: 'off', dailyTimeIst: '16:00' })).toBe('Auto refresh: off');
    expect(describeSchedule({ mode: 'daily', dailyTimeIst: '08:45' })).toBe('Auto refresh: daily at 08:45 IST');
    expect(describeSchedule({ mode: 'interval', dailyTimeIst: '08:45' })).toBe('Auto refresh: off');
  });
});
