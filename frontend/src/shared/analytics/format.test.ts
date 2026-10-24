import {
  formatAsOf,
  formatAsOfDay,
  formatCount,
  formatR,
  formatRange,
  formatRate,
  formatRatio,
  formatSigned,
  NOT_AVAILABLE,
} from './format';

const missing = [null, undefined, Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY];

describe('report formatters', () => {
  it.each([formatCount, formatRatio, formatSigned, formatR, formatRate])(
    'print n/a for a missing or non-finite number, never 0 or NaN (%o)',
    (format) => {
      for (const value of missing) expect(format(value)).toBe(NOT_AVAILABLE);
    },
  );

  it('groups the Indian way and rounds counts', () => {
    expect(formatCount(1284)).toBe('1,284');
    expect(formatCount(1234567)).toBe('12,34,567');
    expect(formatCount(0)).toBe('0');
  });

  it('writes the sign as text so a gain or loss never depends on colour', () => {
    expect(formatSigned(0.3)).toBe('+0.30');
    expect(formatSigned(-0.4)).toBe('-0.40');
    expect(formatR(1.25)).toBe('+1.25 R');
    expect(formatR(-0.5)).toBe('-0.50 R');
  });

  it('prints zero, and a figure that rounds to zero, without a sign', () => {
    expect(formatSigned(0)).toBe('0.00');
    expect(formatSigned(-0.001)).toBe('0.00');
    expect(formatR(-0)).toBe('0.00 R');
  });

  it('writes a ratio with two decimals and a fraction as a percentage with one', () => {
    expect(formatRatio(1.8)).toBe('1.80');
    expect(formatRate(0.542)).toBe('54.2%');
    expect(formatRate(0)).toBe('0.0%');
  });

  it('writes a range of calendar dates as written, whatever the viewer zone', () => {
    expect(formatRange({ from: '2024-04-01', to: '2026-04-30' })).toBe('1 Apr 2024 to 30 Apr 2026');
    expect(formatRange(null)).toBe('not stated');
    expect(formatRange(undefined)).toBe('not stated');
  });

  it('writes a stored moment in India time', () => {
    expect(formatAsOf('2026-10-12T09:59:00Z')).toBe('12 Oct 2026, 15:29 IST');
    expect(formatAsOf('2026-10-12T20:00:00Z')).toBe('13 Oct 2026, 01:30 IST');
    expect(formatAsOfDay('2026-10-12T20:00:00Z')).toBe('13 Oct 2026');
    expect(formatAsOf(null)).toBe(NOT_AVAILABLE);
    expect(formatAsOfDay(undefined)).toBe(NOT_AVAILABLE);
  });
});
