import { istDayIso } from '@/shared/lib/format';

/** The earliest acquisition date the API accepts. */
export const minAcquiredOn = '1990-01-01';

/** Today in India as yyyy-MM-dd: the latest acquisition date. */
export const todayIso = (): string => istDayIso();

const isoDay = /^(\d{4})-(\d{2})-(\d{2})$/;

function isRealDay(value: string): boolean {
  const match = isoDay.exec(value);
  if (!match) return false;
  const [, year, month, day] = match;
  const date = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day)));
  return (
    date.getUTCFullYear() === Number(year) &&
    date.getUTCMonth() === Number(month) - 1 &&
    date.getUTCDate() === Number(day)
  );
}

/** Acquisition date text → message (undefined when valid): a real day, not before 1990, not in the future. */
export function validateAcquiredOn(value: string, today: string = todayIso()): string | undefined {
  if (value.trim() === '') return 'Enter a date.';
  if (!isRealDay(value)) return 'Enter a valid date.';
  if (value < minAcquiredOn) return 'The date cannot be before 1 Jan 1990.';
  if (value > today) return 'The date cannot be in the future.';
  return undefined;
}
