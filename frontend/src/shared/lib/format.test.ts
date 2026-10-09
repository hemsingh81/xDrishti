import { formatAgo, formatDuration } from './format';

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
