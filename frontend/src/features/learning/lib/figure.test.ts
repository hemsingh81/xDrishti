import { describe, expect, it } from 'vitest';
import { figure, formatFine, formatFineR, formatFineSigned } from './figure';

describe('figure', () => {
  it('is a value for a finite number, zero included', () => {
    expect(figure(0.25, 'why')).toEqual({ kind: 'value', value: 0.25 });
    expect(figure(0, 'why')).toEqual({ kind: 'value', value: 0 });
  });

  it.each([null, undefined, Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY])(
    'is unavailable with the reason for %s, never a number',
    (value) => {
      expect(figure(value, 'not computed')).toEqual({ kind: 'unavailable', reason: 'not computed' });
    },
  );
});

describe('fine formats', () => {
  it('keeps small figures apart that two decimals would merge', () => {
    expect(formatFineSigned(-0.094)).toBe('-0.094');
    expect(formatFineSigned(-0.091)).toBe('-0.091');
    expect(formatFine(0.0001)).toBe('0.0001');
    expect(formatFineSigned(-0.004)).toBe('-0.004');
  });

  it('prints a sign only where asked', () => {
    expect(formatFine(1.6)).toBe('1.60');
    expect(formatFineSigned(1.6)).toBe('+1.60');
    expect(formatFineR(-0.094)).toBe('-0.094 R');
  });
});
