import { describeChip, formatAge, type ChipInput } from './liveChip';

const now = Date.parse('2026-10-14T10:36:00+05:30');
const secondsAgo = (s: number) => now - s * 1000;
const streaming = { state: 'connected', subscribed: 5, lastTickAt: null };

const input = (overrides: Partial<ChipInput> = {}): ChipInput => ({
  phase: 'open',
  following: true,
  feed: streaming,
  now,
  newestTickAt: secondsAgo(3),
  followingSince: secondsAgo(600),
  ...overrides,
});

describe('the chip of the prices line', () => {
  it('is Live with the time of the tick while it is at most 15 seconds old', () => {
    expect(describeChip(input({ newestTickAt: secondsAgo(15) }))).toMatchObject({
      kind: 'live',
      label: 'Live · 10:35:45 IST',
    });
  });

  it('is still Live, with the age, between 15 and 60 seconds', () => {
    expect(describeChip(input({ newestTickAt: secondsAgo(16) })).label).toBe('Live · last tick 16 s ago');
    expect(describeChip(input({ newestTickAt: secondsAgo(60) })).kind).toBe('live');
  });

  it('is Delayed, with the age, after 60 seconds', () => {
    expect(describeChip(input({ newestTickAt: secondsAgo(61) }))).toMatchObject({
      kind: 'delayed',
      label: 'Delayed · 1 min',
    });
    expect(describeChip(input({ newestTickAt: secondsAgo(3_700) })).label).toBe('Delayed · 1 h 1 min');
  });

  it.each(['connecting', 'reconnecting'])('judges a feed that is %s by its ticks', (state) => {
    expect(describeChip(input({ feed: { ...streaming, state } })).kind).toBe('live');
  });

  it.each(['idle', 'closed', 'paused', 'no_token', 'token_refused', 'flapping', 'not_running'])(
    'is Quotes when the feed is %s, however fresh the last tick',
    (state) => {
      expect(describeChip(input({ feed: { ...streaming, state } }))).toMatchObject({ kind: 'quotes', fallback: false });
    },
  );

  it('is Quotes while the feed state is not known, and a fallback when the hub does not follow', () => {
    expect(describeChip(input({ feed: null })).kind).toBe('quotes');
    expect(describeChip(input({ following: false }))).toMatchObject({ kind: 'quotes', fallback: true });
  });

  it('gives the first tick a minute to arrive, then is Delayed', () => {
    expect(describeChip(input({ newestTickAt: null, followingSince: secondsAgo(60) })).kind).toBe('quotes');
    expect(describeChip(input({ newestTickAt: null, followingSince: secondsAgo(61) }))).toMatchObject({
      kind: 'delayed',
      label: 'Delayed · no tick yet',
    });
  });

  it('is Pre-open before the open and Closed outside the session, whatever the hub says', () => {
    expect(describeChip(input({ phase: 'preopen' })).kind).toBe('preopen');
    expect(describeChip(input({ phase: 'closed' }))).toMatchObject({ kind: 'closed', label: 'Closed' });
    expect(describeChip(input({ phase: 'closed', following: false })).fallback).toBe(false);
  });

  it('describes itself in words for a screen reader', () => {
    expect(describeChip(input()).description).toMatch(/Prices are live/);
    expect(describeChip(input({ feed: null })).description).toMatch(/live feed is not streaming/);
  });
});

describe('formatAge', () => {
  it.each([
    [0, '0 s'],
    [59_999, '59 s'],
    [60_000, '1 min'],
    [3_599_000, '59 min'],
    [7_200_000, '2 h'],
  ])('writes %i ms as %s', (ms, text) => {
    expect(formatAge(ms)).toBe(text);
  });
});
