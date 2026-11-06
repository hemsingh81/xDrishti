import type { MetricState } from '@/shared/analytics';

/**
 * A server figure that may be null or not finite: a finite number is a value; null, NaN or an infinity is `unavailable` with
 * the reason. Never 0.
 */
export const figure = (value: number | null | undefined, reason: string): MetricState =>
  typeof value === 'number' && Number.isFinite(value) ? { kind: 'value', value } : { kind: 'unavailable', reason };

// Small gains and limits (0.0045 per 50 rows, a 0.002 limit, -0.094 against -0.091) must not round to the same two decimals:
// two to four decimals, invariant locale.
const fine = new Intl.NumberFormat('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 4 });
const fineSigned = new Intl.NumberFormat('en-IN', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 4,
  signDisplay: 'exceptZero',
});

/** 0.002 -> "0.002", 0.5 -> "0.50": a quantity with no sign (a limit, a standard error, a design effect). */
export const formatFine = (value: number): string => fine.format(value);

/** 0.0045 -> "+0.0045", -0.1 -> "-0.10": a gain or loss, the sign is text. */
export const formatFineSigned = (value: number): string => fineSigned.format(value);

/** -0.094 -> "-0.094 R": a result in R multiples at the finer precision. */
export const formatFineR = (value: number): string => `${fineSigned.format(value)} R`;
