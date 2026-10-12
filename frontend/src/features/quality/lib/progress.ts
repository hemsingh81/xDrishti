import type { DataJob, QualityPhase } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

export type PhaseKey = 'csv' | 'dhan' | 'aggregate' | 'quality';

const phaseKeys: readonly PhaseKey[] = ['csv', 'dhan', 'aggregate', 'quality'];
const isPhaseKey = (value: string | null): value is PhaseKey => phaseKeys.some((k) => k === value);

/** What the job is doing now, in words, and what its progress counts. */
const doing: Readonly<Record<PhaseKey, { title: string; unit: string }>> = {
  csv: { title: 'Loading the CSV files', unit: 'files' },
  dhan: { title: 'Fetching from Dhan', unit: 'instruments' },
  aggregate: { title: 'Building the aggregates', unit: 'instruments' },
  quality: { title: 'Checking the data', unit: 'instruments' },
};

export interface JobProgress {
  title: string;
  unit: string;
  done: number;
  total: number;
  message: string | null;
}

/** done / total as a percentage, 0 when the total is not known yet. */
export const percentDone = (done: number, total: number): number =>
  total > 0 ? Math.min(100, Math.round((done / total) * 100)) : 0;

/** The phase of a running job that is working now (or the first one still waiting); null when none is known yet. */
export function progressOf(job: DataJob): JobProgress | null {
  const key = isPhaseKey(job.currentPhase) ? job.currentPhase : null;
  if (!key) return null;
  const phase = job[key];
  if (!phase) return null;
  return { ...doing[key], done: phase.done, total: phase.total, message: phase.message };
}

const plural = (count: number, one: string, many: string) => `${formatNumber(count)} ${count === 1 ? one : many}`;

/** "12 passed · 2 warnings · 1 failed" */
export const resultCounts = (q: Pick<QualityPhase, 'passed' | 'warned' | 'failed'>): string =>
  [
    `${formatNumber(q.passed)} passed`,
    plural(q.warned, 'warning', 'warnings'),
    `${formatNumber(q.failed)} failed`,
  ].join(' · ');

/** The sentence for the toast and the screen reader when a job ends. */
export function outcomeText(job: DataJob): string {
  const quality = job.quality ?? null;
  const fix = job.steps.includes('dhan')
    ? 'Re-fetched'
    : job.steps.includes('aggregate')
      ? 'Aggregates refreshed'
      : null;
  if (job.status === 'failed') {
    return `The job failed: ${job.error ?? quality?.message ?? 'see the recent runs for the reason.'}`;
  }
  if (job.status === 'skipped') return `The job was skipped: ${job.error ?? 'no reason was recorded.'}`;
  if (quality?.status !== 'succeeded') return fix ? `${fix}.` : 'The update finished.';
  if (job.instrumentId) {
    const result =
      quality.failed > 0 ? 'still has issues' : quality.warned > 0 ? 'passes with warnings' : 'all checks pass';
    return `${fix ? `${fix} and re-checked` : 'Checked'}: ${result}.`;
  }
  return `${fix ? 'Fix finished' : 'Checks finished'}: ${resultCounts(quality)}.`;
}
