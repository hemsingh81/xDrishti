import { formatDate, formatIstDate, formatIstDateTime } from '@/shared/lib/format';

/**
 * Report formatters. Null-safe (a missing or non-finite number prints "n/a", never 0 or "NaN") and invariant: one locale
 * (en-IN, Indian grouping) and India time for moments, whatever the viewer's locale or zone, so the same stored report reads
 * the same everywhere. Intl formatters are built once.
 */
export const NOT_AVAILABLE = 'n/a';

const count = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 0 });
const ratio = new Intl.NumberFormat('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const signedRatio = new Intl.NumberFormat('en-IN', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  signDisplay: 'exceptZero',
});
const rate = new Intl.NumberFormat('en-IN', {
  style: 'percent',
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

type Maybe = number | null | undefined;
const finite = (value: Maybe): value is number => typeof value === 'number' && Number.isFinite(value);

/** 1284 -> "1,284". */
export const formatCount = (value: Maybe): string => (finite(value) ? count.format(value) : NOT_AVAILABLE);

/** 1.8 -> "1.80": profit factor, a ratio without a unit. */
export const formatRatio = (value: Maybe): string => (finite(value) ? ratio.format(value) : NOT_AVAILABLE);

/** 0.3 -> "+0.30", -0.4 -> "-0.40", 0 -> "0.00": the sign is text, so a gain or loss never depends on colour. */
export const formatSigned = (value: Maybe): string => (finite(value) ? signedRatio.format(value) : NOT_AVAILABLE);

/** Result in R multiples, signed: 1.25 -> "+1.25 R". */
export const formatR = (value: Maybe): string => (finite(value) ? `${signedRatio.format(value)} R` : NOT_AVAILABLE);

/** A fraction as a percentage: 0.542 -> "54.2%". */
export const formatRate = (value: Maybe): string => (finite(value) ? rate.format(value) : NOT_AVAILABLE);

/** "1 Apr 2024 to 30 Apr 2026" from calendar dates (yyyy-MM-dd) as written; no range says "not stated". */
export function formatRange(range: { from: string; to: string } | null | undefined): string {
  return range ? `${formatDate(range.from)} to ${formatDate(range.to)}` : 'not stated';
}

/** "12 Oct 2026, 15:29 IST" for a stored moment, or "n/a". */
export const formatAsOf = (value: string | Date | null | undefined): string =>
  value ? formatIstDateTime(value) : NOT_AVAILABLE;

/** "12 Oct 2026" for the India-time day of a stored moment, or "n/a". */
export const formatAsOfDay = (value: string | Date | null | undefined): string =>
  value ? formatIstDate(value) : NOT_AVAILABLE;
