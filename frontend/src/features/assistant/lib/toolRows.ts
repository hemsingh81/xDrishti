import type { EvidenceProps, MetricState } from '@/shared/analytics';

type Rec = Record<string, unknown>;
const isRec = (v: unknown): v is Rec => typeof v === 'object' && v !== null && !Array.isArray(v);

const stateNames = new Set(['value', 'provisional', 'insufficient', 'unavailable', 'not_evaluable', 'empty']);

/** A cell shaped `{state, number, reason}` (the report engine's MetricView). */
export function isMetricShaped(v: unknown): v is Rec & { state: string } {
  return isRec(v) && typeof v.state === 'string' && stateNames.has(v.state) && 'number' in v;
}

const finite = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);

/** The honest state of a metric-shaped cell: a non-value never carries a number. */
export function metricFromCell(cell: Rec & { state: string }): MetricState {
  const reason = typeof cell.reason === 'string' && cell.reason ? cell.reason : undefined;
  switch (cell.state) {
    case 'value':
      return finite(cell.number)
        ? { kind: 'value', value: cell.number }
        : { kind: 'unavailable', reason: 'No number was given.' };
    case 'provisional':
      return finite(cell.number)
        ? { kind: 'provisional', value: cell.number, reason: reason ?? 'Provisional' }
        : { kind: 'unavailable', reason: 'No number was given.' };
    case 'insufficient':
      return finite(cell.n) && finite(cell.min)
        ? { kind: 'insufficient', n: cell.n, min: cell.min }
        : { kind: 'unavailable', reason: reason ?? 'Insufficient data' };
    case 'not_evaluable':
      return { kind: 'not_evaluable', reason: reason ?? 'Not evaluable' };
    case 'empty':
      return { kind: 'empty', ...(reason ? { reason } : {}) };
    default:
      return { kind: 'unavailable', reason: reason ?? 'Not available' };
  }
}

/** Any other cell as text; objects and arrays as compact JSON text (inert). */
export function cellText(v: unknown): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'string') return v;
  if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  return JSON.stringify(v);
}

export interface ToolTable {
  columns: { id: string; metric: boolean }[];
  rows: Rec[];
}

const maxColumns = 30;

/** The envelope's rows as a table: the keys of the rows are the columns; a column holding a metric-shaped cell is a metric column. */
export function tableOf(result: unknown): ToolTable {
  const rows = isRec(result) && Array.isArray(result.rows) ? result.rows : [];
  const records: Rec[] = rows.map((r) => (isRec(r) ? r : { value: r }));
  const ids: string[] = [];
  for (const r of records) for (const k of Object.keys(r)) if (!ids.includes(k) && ids.length < maxColumns) ids.push(k);
  return {
    columns: ids.map((id) => ({ id, metric: records.some((r) => isMetricShaped(r[id])) })),
    rows: records,
  };
}

/** A cell of a metric column: a metric-shaped cell, a plain number, or (otherwise) not available, never a zero. */
export function metricCellOf(v: unknown): MetricState {
  if (isMetricShaped(v)) return metricFromCell(v);
  if (finite(v)) return { kind: 'value', value: v };
  return { kind: 'unavailable', reason: v === null || v === undefined ? 'Not reported.' : cellText(v) };
}

/** The evidence block of an envelope (`{basis, n, dateRange:"a..b", population[], limits[]}`); null when absent. */
export function evidenceProps(evidence: unknown): Omit<EvidenceProps, 'compact'> | null {
  if (!isRec(evidence)) return null;
  const basis = evidence.basis === 'gross' || evidence.basis === 'net' ? evidence.basis : 'unknown';
  const parts = typeof evidence.dateRange === 'string' ? evidence.dateRange.split('..') : [];
  const [from, to] = parts;
  const strings = (v: unknown) => (Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : undefined);
  const population = strings(evidence.population);
  const limits = strings(evidence.limits);
  return {
    n: finite(evidence.n) ? evidence.n : null,
    range: from && to ? { from, to } : null,
    basis,
    ...(population ? { population } : {}),
    ...(limits ? { limits } : {}),
  };
}

export function totalRowsOf(result: unknown): number | null {
  return isRec(result) && finite(result.totalRows) ? result.totalRows : null;
}

export function asOfOf(result: unknown): string | null {
  return isRec(result) && typeof result.asOf === 'string' ? result.asOf : null;
}
