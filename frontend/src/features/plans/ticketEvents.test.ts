import { buildCalendar, buildEventFilter, buildMeeting } from '@/test/eventsFixtures';
import { isNearSession, ticketEvents, type TicketEventsInput } from './lib/ticketEvents';

const withDays = (days: number) =>
  buildCalendar({ meetings: [buildMeeting({ symbol: 'INFY', days, eventSession: '2026-11-04' })] });
const on = (n = 2) => buildEventFilter({ enabled: true, resultsWithinDays: n });
const input = (over: Partial<TicketEventsInput> = {}): TicketEventsInput => ({
  symbol: 'INFY',
  calendar: buildCalendar(),
  filter: buildEventFilter(),
  filterFailed: false,
  ...over,
});

describe('ticketEvents: the Events line of a ticket', () => {
  it.each([
    [0, 'results today'],
    [1, 'results in 1 session'],
    [2, 'results in 2 sessions'],
    [3, 'results in 3 sessions'],
    [4, 'results in 4 sessions'],
  ])('says days %i as of the day the calendar was read, with the event session and intimation', (days, words) => {
    const line = ticketEvents(input({ calendar: withDays(days) }));
    expect(line.results).toBe(`${words} as of 2 Nov (Wed 4 Nov 2026, intimated 28 Oct 2026)`);
    expect(line.filter).toBeNull();
    expect(line.unknown).toBe(false);
  });

  it('is unavailable with "no calendar entry" for an instrument the calendar lists without one', () => {
    const calendar = buildCalendar({ instrumentsWithoutEntry: { count: 1, symbols: ['INFY'] } });
    expect(ticketEvents(input({ calendar })).results).toBe('unavailable — no calendar entry (as of 2 Nov)');
  });

  it.each([
    ['only_cancelled', 'only cancelled meetings in the calendar'],
    ['no_upcoming_entry', 'the last meeting has passed and the next is not announced'],
    ['no_entry', 'no calendar entry'],
  ] as const)('words the closed reason %s', (reason, words) => {
    expect(ticketEvents(input({ reason })).results).toBe(`unavailable — ${words} (as of 2 Nov)`);
  });

  it.each([
    ['a calendar cut short', buildCalendar({ truncated: true })],
    ['past the 300-symbol cap', buildCalendar({ instrumentsWithoutEntry: { count: 400, symbols: ['AAA'] } })],
    ['outside the range', buildCalendar()],
  ])('says "not known from the calendar list" for a symbol %s, never "no upcoming meeting"', (_name, calendar) => {
    const line = ticketEvents(input({ calendar }));
    expect(line.results).toBe('unavailable — not known from the calendar list (as of 2 Nov)');
    expect(line.results).not.toMatch(/no upcoming meeting/);
    expect(line.unknown).toBe(true);
  });

  it('with the filter ON says it would exclude a meeting inside N today, and nothing for one outside', () => {
    expect(ticketEvents(input({ calendar: withDays(2), filter: on(2) })).filter).toBe(
      'the filter would exclude it today (N=2)',
    );
    expect(ticketEvents(input({ calendar: withDays(3), filter: on(2) })).filter).toBeNull();
  });

  it('with the filter ON says it would hold an instrument whose event is unavailable, or a caveat when event is optional', () => {
    expect(ticketEvents(input({ filter: on() })).filter).toBe('the filter would hold it (event unavailable)');
    expect(ticketEvents(input({ filter: on(), optionalCaveat: true })).filter).toMatch(/^caveat: event unavailable/);
  });

  it('says so when the filter state could not be read, and never claims what the plan decided', () => {
    const line = ticketEvents(input({ calendar: withDays(1), filter: undefined, filterFailed: true }));
    expect(line.filter).toBe('event filter state unavailable');
    expect(`${line.results} ${line.filter}`).not.toMatch(/excluded by|held:/);
  });
});

describe('isNearSession', () => {
  it.each([
    ['2026-11-02', true],
    ['2026-11-03', true],
    ['2026-11-05', true],
    ['2026-11-06', false],
    ['2026-11-01', false],
  ])('plan for %s from 2 Nov: %s', (forDate, near) => {
    expect(isNearSession(forDate, '2026-11-02')).toBe(near);
  });
});
