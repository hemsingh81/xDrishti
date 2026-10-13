import { untilText } from './countdown';

const now = Date.parse('2026-10-13T08:35:00Z');
const after = (minutes: number) => now + minutes * 60_000;

describe('untilText', () => {
  it.each([
    [1, 'in 1 min'],
    [45, 'in 45 min'],
    [59, 'in 59 min'],
    [60, 'in 1 h'],
    [105, 'in 1 h 45 min'],
    [23 * 60 + 59, 'in 23 h 59 min'],
    [24 * 60, 'in 1 d'],
    [24 * 60 + 3 * 60, 'in 1 d 3 h'],
    [3 * 24 * 60 + 30, 'in 3 d'],
  ])('says %i minutes ahead as "%s"', (minutes, text) => {
    expect(untilText(after(minutes), now)).toBe(text);
  });

  it('rounds a part of a minute up, so it never says "in 0 min"', () => {
    expect(untilText(now + 1, now)).toBe('in 1 min');
    expect(untilText(now + 61_000, now)).toBe('in 2 min');
  });

  it('says "due now" at the moment and after it', () => {
    expect(untilText(now, now)).toBe('due now');
    expect(untilText(now - 5 * 60_000, now)).toBe('due now');
  });
});
