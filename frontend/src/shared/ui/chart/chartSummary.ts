import { chartDates } from './chartData';
import type { ChartSeries } from './types';

interface SummaryInput {
  label: string;
  series: readonly ChartSeries[];
  valueFormatter: (value: number) => string;
  dateFormatter: (date: string) => string;
}

/**
 * The chart in words, for assistive technology: what it shows, its date range and, per line, the first and last value
 * with their dates and the lowest and highest value. The table is the full alternative.
 */
export function chartSummary({ label, series, valueFormatter, dateFormatter }: SummaryInput): string {
  const dates = chartDates(series);
  const first = dates[0];
  const last = dates[dates.length - 1];
  if (first === undefined || last === undefined) return `${label}: no data.`;
  const parts = series.map((s) => {
    const points = s.data.filter((p): p is readonly [string, number] => p[1] !== null);
    const start = points[0];
    const end = points[points.length - 1];
    if (!start || !end) return `${s.name}: no values.`;
    const values = points.map((p) => p[1]);
    return (
      `${s.name}: ${valueFormatter(start[1])} on ${dateFormatter(start[0])}, ` +
      `${valueFormatter(end[1])} on ${dateFormatter(end[0])}, ` +
      `lowest ${valueFormatter(Math.min(...values))}, highest ${valueFormatter(Math.max(...values))}.`
    );
  });
  const count = dates.length === 1 ? '1 date' : `${String(dates.length)} dates`;
  return `${label}, ${count} from ${dateFormatter(first)} to ${dateFormatter(last)}. ${parts.join(' ')}`;
}
