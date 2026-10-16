import {
  formatAgo,
  formatDate,
  formatDuration,
  formatHoursMinutes,
  formatInr,
  formatInrPerShare,
  formatInrPrecise,
  formatCompact,
  formatIstDate,
  formatIstDateTime,
  formatIstDayAndTime,
  formatIstDayMonth,
  formatIstTime,
  formatIstTimeAndDay,
  formatPercent,
  formatShare,
  formatPrice,
  formatDayMonth,
  formatMonthYear,
  formatWeekdayDate,
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

describe('formatInrPerShare', () => {
  it('shows up to four decimals, trims trailing zeros and never shows fewer than two', () => {
    expect(formatInrPerShare(0.0125)).toBe('₹0.0125');
    expect(formatInrPerShare(31.25)).toBe('₹31.25');
    expect(formatInrPerShare(2.5)).toBe('₹2.50');
    expect(formatInrPerShare(12)).toBe('₹12.00');
    expect(formatInrPerShare(1.1)).toBe('₹1.10');
    expect(formatInrPerShare(1_234.5675)).toBe('₹1,234.5675');
  });
});

describe('formatPercent', () => {
  it('shows two decimals', () => {
    expect(formatPercent(16.1)).toBe('16.10');
    expect(formatPercent(1_234.5)).toBe('1,234.50');
  });
});

describe('formatShare', () => {
  it('shows one decimal', () => {
    expect(formatShare(32.14)).toBe('32.1');
    expect(formatShare(100)).toBe('100.0');
    expect(formatShare(0)).toBe('0.0');
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

describe('formatWeekdayDate', () => {
  it('adds the weekday to a calendar date, without shifting it by the time zone', () => {
    expect(formatWeekdayDate('2026-10-20')).toBe('Tue 20 Oct 2026');
    expect(formatWeekdayDate('2026-11-14')).toBe('Sat 14 Nov 2026');
  });
});

describe('chart axis dates', () => {
  it('writes a calendar date as day and month, or as month and year, without a zone shift', () => {
    expect(formatDayMonth('2026-10-13')).toBe('13 Oct');
    expect(formatMonthYear('2026-10-13')).toBe('Oct 2026');
    expect(formatMonthYear('2025-01-01')).toBe('Jan 2025');
  });
});

describe('formatIstTimeAndDay', () => {
  it("shows the time and day in India time, whatever the viewer's zone", () => {
    expect(formatIstTimeAndDay('2026-10-10T22:00:00Z')).toBe('03:30, 11 Oct');
    expect(formatIstTimeAndDay('2026-10-11T06:30:00Z')).toBe('12:00, 11 Oct');
  });
});

describe('formatIstDayAndTime', () => {
  it("shows the weekday, day and time in India time, whatever the viewer's zone", () => {
    expect(formatIstDayAndTime('2026-10-13T10:20:00Z')).toBe('Tue 13 Oct 15:50');
    expect(formatIstDayAndTime('2026-10-12T15:52:00+05:30', { weekday: false })).toBe('12 Oct 15:52');
    expect(formatIstDayAndTime('2026-10-12T19:00:00Z', { weekday: false })).toBe('13 Oct 00:30');
  });
});

describe('formatIstDateTime and formatIstDate', () => {
  it("show a moment in India time with the zone named, whatever the viewer's zone", () => {
    expect(formatIstDateTime('2026-10-12T09:59:00Z')).toBe('12 Oct 2026, 15:29 IST');
    expect(formatIstDateTime('2026-10-12T15:29:00+05:30')).toBe('12 Oct 2026, 15:29 IST');
    expect(formatIstDate('2026-10-11T19:00:00Z')).toBe('12 Oct 2026');
  });
});

describe('formatIstDayMonth and formatIstTime', () => {
  it('show the day and the time of day in India time, whatever the viewer zone', () => {
    expect(formatIstDayMonth('2026-10-11T19:00:00Z')).toBe('12 Oct');
    expect(formatIstDayMonth(Date.parse('2026-10-10T09:55:00Z'))).toBe('10 Oct');
    expect(formatIstTime('2026-10-13T05:05:00Z')).toBe('10:35');
    expect(formatIstTime('2026-10-13T18:30:00Z')).toBe('00:00');
  });
});

describe('formatPrice', () => {
  it('always shows two decimals with Indian grouping and no currency sign', () => {
    expect(formatPrice(2456.3)).toBe('2,456.30');
    expect(formatPrice(123456)).toBe('1,23,456.00');
  });
});

describe('formatCompact', () => {
  it.each([
    [999, '999'],
    [8_234, '8.2K'],
    [123_456, '1.2L'],
    [41_234_567, '4.1Cr'],
  ])('shortens %d to %s', (value, expected) => {
    expect(formatCompact(value)).toBe(expected);
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
