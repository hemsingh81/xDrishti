import { validateAcquiredOn } from './dates';

describe('dates', () => {
  const today = '2026-10-11';

  it.each([
    ['2025-01-01', undefined],
    ['2026-10-11', undefined],
    ['1990-01-01', undefined],
    ['', 'Enter a date.'],
    ['  ', 'Enter a date.'],
    ['2026-10-12', 'The date cannot be in the future.'],
    ['1989-12-31', 'The date cannot be before 1 Jan 1990.'],
    ['2025-02-30', 'Enter a valid date.'],
    ['11/10/2026', 'Enter a valid date.'],
  ])('checks "%s"', (value, message) => {
    expect(validateAcquiredOn(value, today)).toBe(message);
  });
});
