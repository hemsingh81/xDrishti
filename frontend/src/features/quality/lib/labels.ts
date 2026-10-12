import type { QualityRun } from '@/shared/api';
import type { QualityStatus } from './filters';

/** The timeframes the aggregates check compares (the 1-minute bars are the source, not a derived timeframe). */
export const aggregateTimeframes = ['5m', '10m', '15m', '30m', '1h', '1D', '1W', '1M'] as const;
export type AggregateTimeframe = (typeof aggregateTimeframes)[number];

export const isAggregateTimeframe = (value: string): value is AggregateTimeframe =>
  aggregateTimeframes.some((t) => t === value);

export const statusLabels: Readonly<Record<QualityStatus, string>> = {
  pass: 'Pass',
  warning: 'Warning',
  fail: 'Fail',
  notChecked: 'Not checked',
};

/** A status word for any value the API sends; "notAvailable" is a check that cannot be made yet. */
export function statusLabel(status: string): string {
  if (status in statusLabels) return statusLabels[status as QualityStatus];
  return status === 'notAvailable' ? 'Not available' : status;
}

/** The status filter chips of the summary ribbon: plural words, one per status. */
export const ribbonLabels: Readonly<Record<QualityStatus, string>> = {
  pass: 'Passed',
  warning: 'Warnings',
  fail: 'Failed',
  notChecked: 'Not checked',
};

export const columnLabels = {
  symbol: 'Symbol',
  name: 'Name',
  status: 'Status',
  score: 'Score',
  coverage: 'Coverage',
  missingMinutes: 'Missing minutes',
  checkedAt: 'Checked (IST)',
} as const;

const sourceLabels: Readonly<Record<string, string>> = { csv: 'CSV', dhan: 'Dhan', mixed: 'CSV + Dhan' };
export const sourceLabel = (source: string | null): string => (source ? (sourceLabels[source] ?? source) : '—');

const triggerLabels: Readonly<Record<string, string>> = {
  manual: 'Manual',
  daily: 'Automatic',
  after_job: 'After update',
};
export const triggerLabel = (run: Pick<QualityRun, 'trigger'>): string => triggerLabels[run.trigger] ?? run.trigger;

const runStatusLabels: Readonly<Record<string, string>> = {
  running: 'Running',
  succeeded: 'Succeeded',
  failed: 'Failed',
};
export const runStatusLabel = (status: string): string => runStatusLabels[status] ?? status;

/** The checks of an instrument, in the order the drawer lists them. */
export const checkTitles = {
  completeness: 'Completeness',
  sanity: 'Bar sanity',
  spikes: 'Spikes',
  aggregates: 'Aggregates vs 1-minute',
  freshness: 'Freshness',
  officialDaily: 'Official daily candle',
} as const;

export type StatusColor = 'success' | 'warning' | 'error' | 'default';

/** The MUI colour of a status (always shown together with an icon and a word). */
export const statusColor = (status: string): StatusColor =>
  status === 'pass' ? 'success' : status === 'warning' ? 'warning' : status === 'fail' ? 'error' : 'default';
