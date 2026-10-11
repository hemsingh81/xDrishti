import {
  formatAgo,
  formatDate,
  formatDuration,
  formatHoursMinutes,
  formatInr,
  formatInrPrecise,
  formatIstTimeAndDay,
  formatPercent,
} from './format';

describe('formatDuration', () => {
  it.each([
    [5, '5s'],
    [65, '1m 5s'],
    [3_660, '1h 1m'],
    [90_000, '1d 1h'],
    [-3, '0s'],
  ])('formats %d seconds as %s', (seconds, expected) => {
    expect(formatDuration(seconds)).toBe(expected);
  });
});

describe('formatAgo', () => {
  it('describes a recent past time', () => {
    const now = new Date('2026-10-08T10:00:00Z');
    expect(formatAgo('2026-10-08T09:59:30Z', now)).toBe('30 seconds ago');
    expect(formatAgo('2026-10-08T09:55:00Z', now)).toBe('5 minutes ago');
  });
});

describe('formatInr', () => {
  it('groups digits the Indian way', () => {
    expect(formatInr(200_000)).toBe('₹2,00,000');
    expect(formatInr(1_000)).toBe('₹1,000');
  });
});

describe('formatInrPrecise', () => {
  it('keeps the paise and groups the Indian way', () => {
    expect(formatInrPrecise(2_456.3)).toBe('₹2,456.30');
    expect(formatInrPrecise(12_34_567.891)).toBe('₹12,34,567.89');
  });
});

describe('formatPercent', () => {
  it('shows two decimals', () => {
    expect(formatPercent(16.1)).toBe('16.10');
    expect(formatPercent(1_234.5)).toBe('1,234.50');
  });
});

describe('formatDate', () => {
  it('shows a calendar date as written, without shifting it by the time zone', () => {
    expect(formatDate('2027-10-09')).toBe('9 Oct 2027');
  });

  it('shows the day of a timestamp', () => {
    expect(formatDate('2026-10-11T06:00:00Z')).toMatch(/Oct 2026/);
  });
});

describe('formatIstTimeAndDay', () => {
  it("shows the time and day in India time, whatever the viewer's zone", () => {
    expect(formatIstTimeAndDay('2026-10-10T22:00:00Z')).toBe('03:30, 11 Oct');
    expect(formatIstTimeAndDay('2026-10-11T06:30:00Z')).toBe('12:00, 11 Oct');
  });
});

describe('formatHoursMinutes', () => {
  it.each([
    [30, 'less than a minute'],
    [60, '1 min'],
    [61, '2 min'],
    [45 * 60, '45 min'],
    [2 * 3_600, '2 h'],
    [80 * 60, '1 h 20 min'],
    [3 * 3_600 + 9 * 60 + 1, '3 h 10 min'],
  ])('formats %d seconds as %s', (seconds, expected) => {
    expect(formatHoursMinutes(seconds)).toBe(expected);
  });
});
