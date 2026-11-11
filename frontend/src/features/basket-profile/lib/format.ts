import { formatRate, formatRatio } from '@/shared/analytics';

const percentPoints = new Intl.NumberFormat('en-IN', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  signDisplay: 'exceptZero',
});
const crore = new Intl.NumberFormat('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const whole = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 0 });
const level = new Intl.NumberFormat('en-IN', { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** A number already in percent: 4 -> "+4.00%". */
export const percentOf = (value: number): string => `${percentPoints.format(value)}%`;

/** A fraction: 0.054 -> "5.4%". */
export const fractionOf = (value: number): string => formatRate(value);

/** A plain ratio: beta 1.234 -> "1.23". */
export const ratioOf = (value: number): string => formatRatio(value);

/** Rupees in crore: 25,00,00,000 -> "₹25.00 Cr". */
export const croreOf = (value: number): string => `₹${crore.format(value / 10_000_000)} Cr`;

export const countOf = (value: number): string => whole.format(value);

/** An index level: 104.25 -> "104.3". */
export const levelOf = (value: number): string => level.format(value);

/** Days as "12 sessions". */
export const sessionsOf = (value: number): string => `${whole.format(value)} sessions`;
