/** The ranges of the history card: the chips 1M · 3M · 6M · 1Y · All. */
export const historyRanges = ['1M', '3M', '6M', '1Y', 'All'] as const;
export type HistoryRange = (typeof historyRanges)[number];

export const defaultHistoryRange: HistoryRange = 'All';

const monthsBack: Record<Exclude<HistoryRange, 'All'>, number> = { '1M': 1, '3M': 3, '6M': 6, '1Y': 12 };

const daysIn = (year: number, month: number): number => new Date(Date.UTC(year, month + 1, 0)).getUTCDate();

/** `iso` (yyyy-MM-dd) moved by whole months, the day clamped to the target month: 31 Mar − 1 month = 28 Feb. */
export function addMonths(iso: string, months: number): string {
  const [year = 0, month = 1, day = 1] = iso.split('-').map(Number);
  const target = new Date(Date.UTC(year, month - 1 + months, 1));
  const y = target.getUTCFullYear();
  const m = target.getUTCMonth();
  const d = Math.min(day, daysIn(y, m));
  return `${String(y).padStart(4, '0')}-${String(m + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
}

/**
 * The `from` and `to` the API takes for a range, counted back from today in India (`today` is an IST yyyy-MM-dd).
 * "All" sends neither: the server returns the whole history. The resolution is never sent; the server picks it.
 */
export function rangeParams(range: HistoryRange, today: string): { from?: string; to?: string } {
  if (range === 'All') return {};
  return { from: addMonths(today, -monthsBack[range]), to: today };
}
