import { scoreText } from './system';

describe('scoreText', () => {
  it('prints a ratio with two decimals', () => {
    expect(scoreText(0.0213, 'ratio')).toBe('0.02');
  });

  it('prints a share as a percent with two decimals', () => {
    expect(scoreText(0.021, 'percent')).toBe('2.10 %');
  });

  it('prints rupees in crore with two decimals', () => {
    expect(scoreText(100_000_000, 'rupees')).toBe('Rs 10.00 Cr');
  });
});
