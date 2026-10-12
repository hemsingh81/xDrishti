import type { AggregatePhase, CsvPhase, DataJob, DhanPhase } from '@/shared/api';
import { formatCompact, formatNumber } from '@/shared/lib/format';

const plural = (count: number, one: string, many: string) => `${formatNumber(count)} ${count === 1 ? one : many}`;

/** "39 repaired, 0 rejected": what the row rules did in the files read. */
export const repairLine = (csv: CsvPhase): string =>
  `${formatNumber(csv.repaired)} repaired, ${formatNumber(csv.rejected)} rejected`;

/** "214 files loaded · 4.1Cr rows" — the outcome of the CSV phase. */
export function csvResult(csv: CsvPhase): string {
  const parts = [
    plural(csv.filesLoaded, 'file loaded', 'files loaded'),
    csv.filesUnchanged > 0 ? `${formatNumber(csv.filesUnchanged)} unchanged` : null,
    `${formatCompact(csv.rows)} rows`,
  ];
  return parts.filter((part): part is string => part !== null).join(' · ');
}

/** "120 instruments · 4,500 bars added" — the outcome of the Dhan phase. */
export function dhanResult(dhan: DhanPhase): string {
  const added = dhan.inserted + dhan.updated;
  return `${plural(dhan.instrumentsFetched, 'instrument', 'instruments')} · ${plural(added, 'bar added', 'bars added')}`;
}

/** "1.2L bars written" — the outcome of the aggregate phase. */
export const aggregateResult = (aggregate: AggregatePhase): string =>
  `${formatCompact(aggregate.buckets)} bars written${aggregate.full ? ' (full rebuild)' : ''}`;

/** The key counts of a finished job for the runs list ("214 files, 4.1Cr rows · 4,500 bars from Dhan · …"). */
export function jobCounts(job: DataJob): string[] {
  const counts: string[] = [];
  if (job.csv.status === 'succeeded') counts.push(csvResult(job.csv));
  if (job.dhan.status === 'succeeded') counts.push(dhanResult(job.dhan));
  if (job.aggregate.status === 'succeeded') counts.push(aggregateResult(job.aggregate));
  return counts;
}
