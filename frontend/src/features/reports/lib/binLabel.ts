import { formatSigned } from '@/shared/analytics';

/** The words of a bin of R from its edges; an open end says so. */
export const edge = (lower: number | null, upper: number | null): string =>
  lower === null
    ? `below ${formatSigned(upper)}`
    : upper === null
      ? `${formatSigned(lower)} and above`
      : `${formatSigned(lower)} to ${formatSigned(upper)}`;
