import { carriesNumber, metricOf, nonValueKinds, resolveMetric, type MetricState } from './metric';

describe('metricOf', () => {
  it('makes a finite number a value, zero included', () => {
    expect(metricOf(1.5)).toEqual({ kind: 'value', value: 1.5 });
    expect(metricOf(0)).toEqual({ kind: 'value', value: 0 });
    expect(metricOf(-2)).toEqual({ kind: 'value', value: -2 });
  });

  it.each([null, undefined, Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY])(
    'makes %s unavailable with a reason, never a number',
    (missing) => {
      const state = metricOf(missing, 'Not in the response');
      expect(state).toEqual({ kind: 'unavailable', reason: 'Not in the response' });
      expect(carriesNumber(state)).toBe(false);
    },
  );
});

describe('resolveMetric', () => {
  it('keeps real values and every non-value as they are', () => {
    const states: MetricState[] = [
      { kind: 'value', value: 0 },
      { kind: 'provisional', value: 2, reason: 'Provisional plan' },
      { kind: 'insufficient', n: 3, min: 30 },
      { kind: 'empty' },
      { kind: 'loading' },
    ];
    for (const state of states) expect(resolveMetric(state)).toBe(state);
  });

  it.each([Number.NaN, Number.POSITIVE_INFINITY])('turns a %s figure into unavailable', (value) => {
    expect(resolveMetric({ kind: 'value', value }).kind).toBe('unavailable');
    expect(resolveMetric({ kind: 'provisional', value, reason: 'x' }).kind).toBe('unavailable');
  });
});

describe('carriesNumber', () => {
  it('is true for exactly the kinds that carry a value', () => {
    expect(carriesNumber({ kind: 'value', value: 1 })).toBe(true);
    expect(carriesNumber({ kind: 'provisional', value: 1, reason: 'r' })).toBe(true);
    expect(nonValueKinds).toHaveLength(6);
  });
});
