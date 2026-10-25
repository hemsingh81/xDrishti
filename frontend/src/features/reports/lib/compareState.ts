import {
  DEFAULT_COLUMNS,
  DEFAULT_METRIC,
  DEFAULT_ROWS,
  MAX_BASKETS,
  pivotDimensions,
  pivotMetrics,
  type PivotDimension,
  type PivotMetric,
} from './pivotLimits';

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The compared basket ids from `cmp=` (malformed ids and repeats dropped; at most six). */
export function readCompared(text: string | null): string[] {
  const ids = (text ?? '')
    .split(',')
    .map((id) => id.trim())
    .filter((id) => uuid.test(id));
  return [...new Set(ids)].slice(0, MAX_BASKETS);
}

/** The pivot axes and metric from the address; anything unknown falls back to the default. */
export function readPivot(text: (name: string) => string | null): {
  rows: PivotDimension;
  columns: PivotDimension;
  metric: PivotMetric;
} {
  const dim = (name: string, fallback: PivotDimension) =>
    pivotDimensions.find((d) => d.id === text(name))?.id ?? fallback;
  const rows = dim('rows', DEFAULT_ROWS);
  let columns = dim('columns', DEFAULT_COLUMNS);
  // The same dimension twice is refused: the columns give way.
  if (columns === rows) columns = pivotDimensions.find((d) => d.id !== rows)?.id ?? DEFAULT_COLUMNS;
  return { rows, columns, metric: pivotMetrics.find((m) => m.id === text('metric'))?.id ?? DEFAULT_METRIC };
}
