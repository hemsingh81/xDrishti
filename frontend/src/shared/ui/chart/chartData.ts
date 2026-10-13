import { formatDayMonth, formatMonthYear } from '@/shared/lib/format';
import type { ChartSeries } from './types';

/** Every date of every series, once, in order: the x axis of the chart and the rows of the table. */
export function chartDates(series: readonly ChartSeries[]): string[] {
  const dates = new Set<string>();
  for (const s of series) for (const [date] of s.data) dates.add(date);
  return [...dates].sort();
}

/** The values of a series on the given dates (null where it has none). */
export function alignedValues(series: ChartSeries, dates: readonly string[]): (number | null)[] {
  const byDate = new Map(series.data);
  return dates.map((date) => byDate.get(date) ?? null);
}

const dayMs = 86_400_000;

/** Axis labels: "13 Oct" for a range of up to about a year, "Oct 2026" beyond. */
export function axisDateLabel(dates: readonly string[]): (date: string) => string {
  const first = dates[0];
  const last = dates[dates.length - 1];
  const spanDays = first && last ? (Date.parse(last) - Date.parse(first)) / dayMs : 0;
  return spanDays > 400 ? formatMonthYear : formatDayMonth;
}
