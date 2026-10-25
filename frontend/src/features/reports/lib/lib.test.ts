import { describe, expect, it } from 'vitest';
import { dataEndOf, presetFrom } from './datePresets';
import { metricFromApi } from './metricFromApi';
import { readFilter, toQuery } from './reportFilter';

const reader = (params: Record<string, string>) => (name: string) => params[name] ?? null;

describe('date presets', () => {
  it('count back from the data end', () => {
    expect(presetFrom('2026-04-30', 30)).toBe('2026-03-31');
    expect(presetFrom('2026-04-30', null)).toBeNull();
  });
  it('take the newest window end', () => {
    expect(dataEndOf(['2026-01-01', '2026-04-30', '2025-12-31'])).toBe('2026-04-30');
    expect(dataEndOf([])).toBeNull();
  });
});

describe('the filter in the address', () => {
  it('drops malformed values and upper-cases symbols', () => {
    const filter = readFilter(
      reader({
        from: '2026-02-31',
        side: 'UP',
        basket: 'nope,00000000-0000-4000-8000-000000000401',
        symbol: 'tcs, in fy',
        runs: 'abc',
        basis: 'x',
      }),
    );
    expect(filter).toMatchObject({ from: null, side: null, symbol: ['TCS'], runs: [], basis: 'gross' });
    expect(filter.basket).toEqual(['00000000-0000-4000-8000-000000000401']);
  });
  it('sends gross as the default and net only when asked', () => {
    const filter = readFilter(reader({ basis: 'net', exit: 'fixed_r' }));
    expect(toQuery(filter)).toEqual({ basis: 'net', exit: 'fixed_r' });
    expect(toQuery(filter, 'gross')).toEqual({ exit: 'fixed_r' });
  });
});

describe('metricFromApi', () => {
  it('maps the six states and never invents a number', () => {
    expect(metricFromApi({ state: 'value', number: 1, reason: null })).toEqual({ kind: 'value', value: 1 });
    expect(metricFromApi({ state: 'value', number: null, reason: null }).kind).toBe('unavailable');
    expect(metricFromApi({ state: 'provisional', number: 1, reason: 'partial' })).toEqual({
      kind: 'provisional',
      value: 1,
      reason: 'partial',
    });
    expect(metricFromApi({ state: 'insufficient', number: null, reason: 'few' }, 5, 30)).toEqual({
      kind: 'insufficient',
      n: 5,
      min: 30,
    });
    expect(metricFromApi({ state: 'insufficient', number: null, reason: 'few' }).kind).toBe('unavailable');
    expect(metricFromApi({ state: 'not_evaluable', number: null, reason: 'x' }).kind).toBe('not_evaluable');
    expect(metricFromApi({ state: 'empty', number: null, reason: null }).kind).toBe('empty');
    expect(metricFromApi({ state: 'mystery', number: 3, reason: null }).kind).toBe('unavailable');
  });
});
