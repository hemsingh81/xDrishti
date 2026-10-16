import { round4 } from './liveFigures';

describe('round4 (the server rounds money to four decimals, halves away from zero)', () => {
  it.each([
    [1.00005, 1.0001],
    [-1.00005, -1.0001],
    [2.5, 2.5],
    [0.12344, 0.1234],
    [-0.12346, -0.1235],
  ])('%d is %d', (value, expected) => {
    expect(round4(value)).toBe(expected);
  });

  it('is 0, never -0, for a small negative that rounds to nothing and for zero', () => {
    expect(Object.is(round4(-0.00001), 0)).toBe(true);
    expect(Object.is(round4(-0), 0)).toBe(true);
    expect(Object.is(round4(0), 0)).toBe(true);
  });
});
