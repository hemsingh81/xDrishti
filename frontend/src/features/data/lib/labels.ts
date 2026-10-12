import type { DataCoverageRow, DataJob } from '@/shared/api';
import { jobCounts } from './summaries';
import type { CoverageStatus, SortField } from './filters';

export const columnLabels: Readonly<Record<SortField, string>> = {
  symbol: 'Symbol',
  name: 'Name',
  firstBar: 'First bar (IST)',
  lastBar: 'Last bar (IST)',
  bars: '1m bars',
};

export const statusLabels: Readonly<Record<CoverageStatus, string>> = {
  current: 'Current',
  behind: 'Behind',
  empty: 'Empty',
};

const sourceLabels: Readonly<Record<string, string>> = { csv: 'CSV', dhan: 'Dhan', mixed: 'CSV + Dhan' };

export const sourceLabel = (source: string | null): string => (source ? (sourceLabels[source] ?? source) : '—');

/** The status chip's text: "Current", "Behind 2 d" or "Empty" (never colour alone). */
export function rowStatusText(row: Pick<DataCoverageRow, 'status' | 'daysBehind'>): string {
  if (row.status === 'behind') return row.daysBehind === null ? 'Behind' : `Behind ${String(row.daysBehind)} d`;
  if (row.status === 'current') return 'Current';
  return 'Empty';
}

/** The sentence for the toast and the screen reader when a job ends. */
export function outcomeText(job: DataJob): string {
  if (job.status === 'failed') return `The update failed: ${job.error ?? 'see Recent runs for the reason.'}`;
  const counts = jobCounts(job);
  return counts.length > 0 ? `Data updated: ${counts.join(' · ')}` : 'Data updated.';
}
