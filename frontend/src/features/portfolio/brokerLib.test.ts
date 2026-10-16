import { buildBrokerCounts, buildBrokerKind, buildBrokerRun, buildBrokerStatus } from '@/test/brokerHistoryFixtures';
import { needsReview, rebuildText, summariseKind } from './lib/brokerText';
import { periodError, signedPrecise, totalsOf } from './lib/ledger';

describe('summariseKind', () => {
  it('reads up to the watermark and counts the new rows', () => {
    expect(summariseKind('trades', buildBrokerKind())).toMatchObject({
      tone: 'success',
      title: 'Trades read up to 11 Oct 2026',
      detail: '4 new',
    });
    const none = buildBrokerKind({ lastRun: buildBrokerRun({ counts: buildBrokerCounts({ stored: 0 }) }) });
    expect(summariseKind('ledger', none).detail).toBe('nothing new');
  });

  it('says so only when more ranges answered "no data" than windows were read (halved and still empty)', () => {
    const run = (emptyByNoData: number, windowsDone: number) =>
      buildBrokerKind({
        lastRun: buildBrokerRun({ counts: buildBrokerCounts({ stored: 0, emptyByNoData, windowsDone }) }),
      });
    expect(summariseKind('trades', run(3, 1)).detail).toBe('nothing new · Dhan answered "no data" for 3 date ranges');
    expect(summariseKind('trades', run(2, 1)).detail).toBe('nothing new · Dhan answered "no data" for 2 date ranges');
    expect(summariseKind('trades', run(1, 1)).detail).toBe('nothing new');
    expect(summariseKind('trades', run(0, 1)).detail).toBe('nothing new');
  });

  it('shows the progress of a running read, and "starting" before the windows are known', () => {
    const running = (windowsDone: number, windowsTotal: number) =>
      buildBrokerKind({
        windowsDone,
        windowsTotal,
        lastRun: buildBrokerRun({
          status: 'running',
          finishedAt: null,
          counts: buildBrokerCounts({ windowsDone, windowsTotal }),
        }),
      });
    expect(summariseKind('trades', running(3, 12))).toMatchObject({ tone: 'info', detail: '3 of 12 windows done' });
    expect(summariseKind('ledger', running(0, 0)).detail).toBe('starting');
  });

  it('names a failed or skipped run with its reason and keeps the watermark', () => {
    const failed = buildBrokerKind({ lastRun: buildBrokerRun({ status: 'failed', error: 'Dhan is down.' }) });
    expect(summariseKind('trades', failed)).toMatchObject({
      tone: 'error',
      title: 'Trades read failed',
      detail: 'Dhan is down. Read up to 11 Oct 2026.',
    });
    const skipped = buildBrokerKind({ watermark: null, lastRun: buildBrokerRun({ status: 'skipped', error: null }) });
    expect(summariseKind('ledger', skipped)).toMatchObject({
      tone: 'warning',
      detail: 'The scheduled read did not run.',
    });
  });

  it('is idle when nothing was ever read', () => {
    expect(summariseKind('ledger', buildBrokerKind({ watermark: null, lastRun: null })).tone).toBe('idle');
  });
});

describe('rebuild text', () => {
  it('words the counts and is silent when nothing was touched', () => {
    expect(rebuildText(buildBrokerStatus({ holdingsRebuilt: 1, holdingsNeedingReview: 2 }))).toBe(
      '1 holding rebuilt, 2 need review',
    );
    expect(rebuildText(buildBrokerStatus({ holdingsRebuilt: 3, holdingsNeedingReview: 1 }))).toBe(
      '3 holdings rebuilt, 1 needs review',
    );
    expect(rebuildText(buildBrokerStatus())).toBeNull();
  });

  it('finds the holdings left for review by their ticker, from the same run as the counts', () => {
    const status = buildBrokerStatus({
      holdingsNeedingReview: 1,
      needsReviewSymbols: ['INFY'],
      // The latest run was skipped (an expired token): it lists nothing, the tickers still stand.
      trades: buildBrokerKind({ lastRun: buildBrokerRun({ status: 'skipped', counts: buildBrokerCounts() }) }),
    });
    expect(needsReview(status, 'INFY')).toBe(true);
    expect(needsReview(status, 'TCS')).toBe(false);
  });
});

describe('ledger helpers', () => {
  it('signs amounts with a real minus and treats less than a paisa as zero', () => {
    expect(signedPrecise(840)).toBe('+₹840.00');
    expect(signedPrecise(-15.93)).toBe('−₹15.93');
    expect(signedPrecise(0.001)).toBe('₹0.00');
  });

  it('always has the five classes in order, with zeros for those the API left out', () => {
    expect(totalsOf([{ class: 'dividend', count: 2, amount: 10 }]).map((t) => [t.class, t.count])).toEqual([
      ['funds_in', 0],
      ['funds_out', 0],
      ['dividend', 2],
      ['charges', 0],
      ['other', 0],
    ]);
  });

  it('finds a period that ends before it starts', () => {
    expect(periodError({ from: '2026-10-08', to: '2026-10-01', cls: 'all' })).not.toBeNull();
    expect(periodError({ from: '2026-10-01', to: '', cls: 'all' })).toBeNull();
  });
});
