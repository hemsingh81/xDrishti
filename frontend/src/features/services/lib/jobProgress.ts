import type { DataJob } from '@/shared/api';

/** What the job is doing now, in words, and what its progress counts. */
const doing = {
  csv: { title: 'Loading the CSV files', unit: 'files' },
  dhan: { title: 'Fetching from Dhan', unit: 'instruments' },
  aggregate: { title: 'Building the aggregates', unit: 'instruments' },
  quality: { title: 'Checking the data', unit: 'instruments' },
} as const;

type PhaseKey = keyof typeof doing;

const isPhaseKey = (value: string | null): value is PhaseKey => value !== null && value in doing;

export interface JobProgress {
  title: string;
  unit: string;
  done: number;
  total: number;
}

/** The phase of a running data job that is working now; null when none is known yet. */
export function progressOf(job: DataJob): JobProgress | null {
  if (!isPhaseKey(job.currentPhase)) return null;
  const phase = job[job.currentPhase];
  return phase ? { ...doing[job.currentPhase], done: phase.done, total: phase.total } : null;
}

/** done / total as a percentage, 0 when the total is not known yet. */
export const percentDone = (done: number, total: number): number =>
  total > 0 ? Math.min(100, Math.round((done / total) * 100)) : 0;
