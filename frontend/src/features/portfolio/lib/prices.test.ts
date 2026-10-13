import {
  buildQuotesService,
  buildScheduleSpec,
  buildServiceRun,
  buildTimeline,
  serviceRunId,
} from '@/test/servicesFixtures';
import { isMarketOpen } from './market';
import { describePrices, quoteMinutes, quotesOffReason } from './prices';

const at = (hhmm: string, day = '2026-10-13') => Date.parse(`${day}T${hhmm}:00+05:30`);
const iso = (hhmm: string, day = '2026-10-13') => `${day}T${hhmm}:00+05:30`;

/** A quote run that started at 10:00:00 and ended at 10:00:04 (the service's default last run). */
const quotes = buildQuotesService();

describe('isMarketOpen', () => {
  const timeline = buildTimeline();

  it.each([
    ['09:14', false],
    ['09:15', true],
    ['12:00', true],
    ['15:30', true],
    ['15:31', false],
    ['23:59', false],
  ])('at %s in India it is %s', (time, open) => {
    expect(isMarketOpen(at(time), timeline)).toBe(open);
  });

  it('is closed on a day that is not a trading day, whatever the time', () => {
    expect(isMarketOpen(at('10:30'), buildTimeline({ tradingDay: false, marketOpen: null, marketClose: null }))).toBe(
      false,
    );
  });

  it('is closed while the timeline is not known', () => {
    expect(isMarketOpen(at('10:30'), undefined)).toBe(false);
  });

  it('reads the India clock, not the clock of the viewer', () => {
    // 04:00 UTC is 09:30 in India
    expect(isMarketOpen(Date.parse('2026-10-13T04:00:00Z'), timeline)).toBe(true);
    // 10:00 UTC is 15:30 in India: still open; 10:01 UTC is not
    expect(isMarketOpen(Date.parse('2026-10-13T10:00:00Z'), timeline)).toBe(true);
    expect(isMarketOpen(Date.parse('2026-10-13T10:01:00Z'), timeline)).toBe(false);
  });
});

describe('quoteMinutes and quotesOffReason', () => {
  it('takes the interval from the schedule and falls back to five minutes', () => {
    expect(quoteMinutes(quotes)).toBe(5);
    expect(
      quoteMinutes(buildQuotesService({ schedule: buildScheduleSpec({ type: 'interval', everyMinutes: 15 }) })),
    ).toBe(15);
    expect(quoteMinutes(null)).toBe(5);
  });

  it('names why quotes are off: paused, failed or skipped, with the run message', () => {
    expect(quotesOffReason(quotes)).toBeNull();
    expect(quotesOffReason(null)).toBeNull();
    expect(quotesOffReason(buildQuotesService({ enabled: false }))).toBe('the service is paused');
    const run = (status: string, message: string | null) =>
      buildQuotesService({ lastRun: buildServiceRun({ id: serviceRunId(8), status, message }) });
    expect(quotesOffReason(run('failed', 'the Data account’s token was refused'))).toBe(
      'the last run failed: the Data account’s token was refused',
    );
    expect(quotesOffReason(run('failed', null))).toBe('the last run failed');
    expect(quotesOffReason(run('skipped', 'The Data account is not connected.'))).toBe(
      'the last run was skipped: The Data account is not connected.',
    );
  });

  it('does not call a running service off', () => {
    expect(quotesOffReason(buildQuotesService({ status: 'running' }))).toBeNull();
  });
});

describe('describePrices', () => {
  it('says the prices are fresh while the newest is at most two intervals old in the session', () => {
    const fresh = describePrices({ newest: iso('10:25'), now: at('10:35'), marketOpen: true, quotes });
    expect(fresh).toEqual({ kind: 'fresh', text: 'Prices as of 10:25 IST · quotes every 5 min' });
    expect(describePrices({ newest: iso('10:24'), now: at('10:35'), marketOpen: true, quotes }).kind).toBe('delayed');
  });

  it('uses the interval of the schedule for what fresh means', () => {
    const slow = buildQuotesService({ schedule: buildScheduleSpec({ type: 'interval', everyMinutes: 15 }) });
    const line = describePrices({ newest: iso('10:10'), now: at('10:35'), marketOpen: true, quotes: slow });
    expect(line).toEqual({ kind: 'fresh', text: 'Prices as of 10:10 IST · quotes every 15 min' });
  });

  it('says the prices are delayed with the time of the last quote, or of the last price when it was a sync', () => {
    const quoted = describePrices({ newest: iso('10:00'), now: at('10:35'), marketOpen: true, quotes });
    expect(quoted).toEqual({ kind: 'delayed', text: 'Prices delayed — last quote 10:00 IST' });
    const synced = describePrices({ newest: iso('09:20'), now: at('10:35'), marketOpen: true, quotes });
    expect(synced).toEqual({ kind: 'delayed', text: 'Prices delayed — last price 09:20 IST' });
  });

  it('gives the date and the source when the market is closed or the prices are from an earlier day', () => {
    expect(describePrices({ newest: iso('15:25', '2026-10-10'), now: at('10:35'), marketOpen: true, quotes })).toEqual({
      kind: 'asOf',
      text: 'As of 10 Oct · from the last sync',
    });
    expect(describePrices({ newest: iso('10:00'), now: at('16:00'), marketOpen: false, quotes })).toEqual({
      kind: 'asOf',
      text: 'As of 13 Oct 10:00 IST · from the last quote',
    });
    expect(describePrices({ newest: iso('09:00'), now: at('16:00'), marketOpen: false, quotes })).toEqual({
      kind: 'asOf',
      text: 'As of 13 Oct 09:00 IST · from the last sync',
    });
  });

  it('puts quotes that are off before everything, with the age of the prices', () => {
    const paused = buildQuotesService({ enabled: false });
    expect(describePrices({ newest: iso('10:00'), now: at('10:35'), marketOpen: true, quotes: paused })).toEqual({
      kind: 'off',
      text: 'Quotes off — the service is paused',
      asOf: 'Prices as of 10:00 IST',
    });
    expect(
      describePrices({ newest: iso('15:25', '2026-10-10'), now: at('10:35'), marketOpen: false, quotes: paused }),
    ).toMatchObject({ kind: 'off', asOf: 'Prices as of 10 Oct' });
  });

  it('works without the service (not known yet) and without any price', () => {
    expect(describePrices({ newest: iso('10:33'), now: at('10:35'), marketOpen: true, quotes: null }).kind).toBe(
      'fresh',
    );
    expect(describePrices({ newest: null, now: at('10:35'), marketOpen: true, quotes })).toEqual({
      kind: 'none',
      text: 'No prices yet',
    });
  });

  it('judges the India day, not the day of the viewer', () => {
    // 20:00 UTC on 12 Oct is 01:30 on 13 Oct in India: a price of 15:25 IST on the 12th is an earlier day
    const now = Date.parse('2026-10-12T20:00:00Z');
    expect(describePrices({ newest: iso('15:25', '2026-10-12'), now, marketOpen: false, quotes }).text).toBe(
      'As of 12 Oct · from the last sync',
    );
  });
});
