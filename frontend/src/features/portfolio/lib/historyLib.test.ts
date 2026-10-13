import { buildHistoryBenchmark, buildTrackResult } from '@/test/historyFixtures';
import { buildReturns } from '@/test/fixtures';
import { alphaOf, benchmarkSeriesName, benchmarkXirrOf, signedPoints } from './benchmark';
import { drawdownDetail, drawdownHint, drawdownText } from './drawdown';
import { addMonths, rangeParams } from './historyRange';
import { historySeries } from './historyView';
import { ApiError } from '@/shared/api';
import { trackBenchmarkToast, trackErrorText, trackHoldingsToast } from './trackText';
import { buildHistory, buildHistoryPoint } from '@/test/historyFixtures';

describe('history ranges', () => {
  it.each([
    ['2026-10-13', -1, '2026-09-13'],
    ['2026-03-31', -1, '2026-02-28'],
    ['2024-03-31', -1, '2024-02-29'],
    ['2026-01-15', -3, '2025-10-15'],
    ['2026-10-13', -12, '2025-10-13'],
    ['2024-02-29', -12, '2023-02-28'],
  ])('moves %s by %i months, clamping the day', (from, months, expected) => {
    expect(addMonths(from, months)).toBe(expected);
  });

  it('counts the chips back from today, and sends nothing for All', () => {
    expect(rangeParams('1M', '2026-10-13')).toEqual({ from: '2026-09-13', to: '2026-10-13' });
    expect(rangeParams('3M', '2026-10-13')).toEqual({ from: '2026-07-13', to: '2026-10-13' });
    expect(rangeParams('6M', '2026-10-13')).toEqual({ from: '2026-04-13', to: '2026-10-13' });
    expect(rangeParams('1Y', '2026-10-13')).toEqual({ from: '2025-10-13', to: '2026-10-13' });
    expect(rangeParams('All', '2026-10-13')).toEqual({});
  });
});

describe('history series', () => {
  it('draws Value, Invested and the benchmark with distinct styles', () => {
    const series = historySeries(buildHistory());
    expect(series.map((s) => [s.id, s.name, s.color, s.dash])).toEqual([
      ['value', 'Value', 'accent', undefined],
      ['invested', 'Invested', 'neutral', 'dashed'],
      ['benchmark', 'Nifty 50 (same cash flows)', 'warn', 'dotted'],
    ]);
  });

  it('names the benchmark line after the index of the master list, else its symbol', () => {
    expect(benchmarkSeriesName(buildHistoryBenchmark({ name: 'Nifty Next 50' }))).toBe(
      'Nifty Next 50 (same cash flows)',
    );
    expect(benchmarkSeriesName(buildHistoryBenchmark({ name: null, symbol: 'NIFTY 100' }))).toBe(
      'NIFTY 100 (same cash flows)',
    );
    expect(benchmarkSeriesName(undefined)).toBe('Nifty 50 (same cash flows)');
  });

  it('leaves the benchmark line out when it is unavailable or has no value', () => {
    expect(
      historySeries(buildHistory({ benchmark: buildHistoryBenchmark({ state: 'unavailable' }) })).map((s) => s.id),
    ).toEqual(['value', 'invested']);
    const noValues = buildHistory({ points: [buildHistoryPoint('2026-10-09', { benchmarkValue: null })] });
    expect(historySeries(noValues).map((s) => s.id)).toEqual(['value', 'invested']);
  });
});

describe('benchmark figures', () => {
  const ok = { benchmarkXirr: 9.1, alpha: 2.2, benchmarkState: 'ok' };

  it('gives the benchmark XIRR when the state is ok', () => {
    expect(benchmarkXirrOf(ok)).toEqual({ value: 9.1 });
  });

  it.each([
    ['unavailable', '—', /Track it and run Update data/],
    ['insufficient', '—', /at least 30 days/],
    ['n_m', 'n/m', /beyond ±1,000 %/],
  ])('explains why there is none (%s)', (benchmarkState, label, reason) => {
    const figure = benchmarkXirrOf({ benchmarkXirr: null, benchmarkState });
    expect(figure).toMatchObject({ label, reason: expect.stringMatching(reason) as string });
  });

  it("prefers the history's own reason for an unavailable benchmark", () => {
    expect(
      benchmarkXirrOf({ benchmarkXirr: null, benchmarkState: 'unavailable' }, { reason: 'Not tracked.' }),
    ).toMatchObject({
      reason: 'Not tracked.',
    });
  });

  it('gives alpha, or the reason it is missing: the benchmark first, then the own XIRR', () => {
    const returns = buildReturns();
    expect(alphaOf({ ...ok, returns })).toEqual({ value: 2.2 });
    expect(alphaOf({ benchmarkXirr: null, alpha: null, benchmarkState: 'unavailable', returns })).toMatchObject({
      reason: expect.stringMatching(/^Alpha needs the benchmark XIRR\./) as string,
    });
    expect(
      alphaOf({
        benchmarkXirr: 9.1,
        alpha: null,
        benchmarkState: 'ok',
        returns: buildReturns({ xirr: null, xirrState: 'insufficient' }),
      }),
    ).toMatchObject({ reason: expect.stringMatching(/^Alpha needs your XIRR\./) as string });
  });

  it('writes a difference of rates in percentage points with its sign', () => {
    expect(signedPoints(2.2)).toBe('+2.20 pp');
    expect(signedPoints(-1.1)).toBe('−1.10 pp');
    expect(signedPoints(0.001)).toBe('0.00 pp');
  });
});

describe('drawdown figures', () => {
  const fall = { pct: 12.5, from: '2025-03-03', to: '2025-04-09' };

  it('writes the fall as a negative percentage with the peak and low dates', () => {
    expect(drawdownText(fall)).toBe('−12.50%');
    expect(drawdownHint(fall)).toBe('3 Mar → 9 Apr');
    expect(
      drawdownDetail(fall, { pct: 8, from: null, to: null }, { rangeLabel: '1Y', benchmarkName: 'Nifty 50' }),
    ).toBe(
      "The portfolio's unit value fell 12.50% from its peak on 3 Mar 2025 to its low on 9 Apr 2025 (range 1Y; cash flows do not count as gains or losses). Nifty 50 fell 8.00% over the same range.",
    );
  });

  it('says there was no fall when the series never fell', () => {
    const flat = { pct: 0, from: null, to: null };
    expect(drawdownText(flat)).toBe('0.00%');
    expect(drawdownHint(flat)).toBe('No fall in this range');
    expect(drawdownDetail(flat, null, { rangeLabel: 'All', benchmarkName: 'Nifty 50' })).toMatch(
      /did not fall in this range \(All\)/,
    );
  });
});

describe('track messages', () => {
  it('says how many holdings were added and what to do next, from the API hint', () => {
    expect(trackHoldingsToast(buildTrackResult({ added: 3, hint: 'Run Update data to build their history.' }))).toEqual(
      {
        message: 'Tracked 3 more holdings — run Update data to build their history',
        severity: 'success',
      },
    );
    expect(trackHoldingsToast(buildTrackResult({ added: 1 })).message).toMatch(/^Tracked 1 more holding —/);
  });

  it('says when everything was tracked already, and what could not be tracked', () => {
    expect(trackHoldingsToast(buildTrackResult({ added: 0, alreadyTracked: 3 }))).toMatchObject({
      message: expect.stringMatching(/^Your holdings are already tracked — /) as string,
      severity: 'info',
    });
    const mixed = trackHoldingsToast(buildTrackResult({ added: 2, notInMaster: 2, retired: 1 }));
    expect(mixed.message).toContain(
      'Tracked 2 more holdings; 2 holdings are not in the instrument master; 1 instrument is retired and skipped',
    );
    expect(
      trackHoldingsToast(buildTrackResult({ requested: 0, added: 0, alreadyTracked: 0, notInMaster: 2, hint: 'x' })),
    ).toEqual({
      message: 'No open holdings could be tracked; 2 holdings are not in the instrument master',
      severity: 'info',
    });
  });

  it('keeps a hint that starts with a name as it is, and drops an empty one', () => {
    expect(
      trackHoldingsToast(buildTrackResult({ added: 1, hint: 'NSE data arrives with Update data.' })).message,
    ).toMatch(/— NSE data arrives with Update data$/);
    expect(trackHoldingsToast(buildTrackResult({ added: 1, hint: ' ' })).message).toBe('Tracked 1 more holding');
  });

  it('answers for the benchmark', () => {
    expect(trackBenchmarkToast(buildTrackResult({ added: 1, hint: 'Run Update data.' }), 'Nifty 50')).toEqual({
      message: 'Tracking Nifty 50 — run Update data',
      severity: 'success',
    });
    expect(
      trackBenchmarkToast(buildTrackResult({ added: 0, alreadyTracked: 1, hint: 'Run Update data.' }), 'Nifty 50'),
    ).toEqual({
      message: 'Nifty 50 is already tracked — run Update data',
      severity: 'info',
    });
  });
});

describe('track failures', () => {
  it("shows the API's own words, or a network text when the server was not reached", () => {
    const problem = new ApiError(409, { title: 'Tracking is locked.', code: 'portfolio.conflict' });
    expect(trackErrorText(problem, 'Fallback.')).toBe('Tracking is locked.');
    expect(trackErrorText(new ApiError(500), 'Fallback.')).toBe('Fallback.');
    expect(trackErrorText(new TypeError('Failed to fetch'), 'Fallback.')).toMatch(/Could not reach the server/);
  });
});
