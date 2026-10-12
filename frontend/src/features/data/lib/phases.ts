import type { DataJob, DataOverview } from '@/shared/api';

export const phaseKeys = ['csv', 'dhan', 'aggregate'] as const;
export type PhaseKey = (typeof phaseKeys)[number];

export const phaseTitles: Readonly<Record<PhaseKey, string>> = {
  csv: 'CSV files',
  dhan: 'Dhan Data API',
  aggregate: 'Aggregates',
};

/** The short step names of the "Recent runs" list. */
export const stepNames: Readonly<Record<PhaseKey, string>> = { csv: 'CSV', dhan: 'Dhan', aggregate: 'Aggregates' };

export const phaseStatusLabels: Readonly<Record<string, string>> = {
  notRequested: 'Not run',
  pending: 'Waiting',
  running: 'Running',
  succeeded: 'Succeeded',
  skipped: 'Skipped',
  failed: 'Failed',
};

export const phaseStatusLabel = (status: string): string => phaseStatusLabels[status] ?? status;

/** A phase that has not finished: show a progress bar (running) or a "waiting" line (pending) instead of a result. */
export const isActive = (status: string | undefined): boolean => status === 'running' || status === 'pending';

/**
 * The phase to show in a step: the one of the job in progress when that job asked for this phase, else the phase of the
 * latest job in which it ran to an end (null when it never did).
 */
export function currentPhase<K extends PhaseKey>(overview: DataOverview, key: K): DataJob[K] | null {
  const live = overview.runningJob?.[key];
  if (live && live.status !== 'notRequested') return live;
  return overview.lastJobs[key]?.[key] ?? null;
}

/** done / total as a percentage, 0 when the total is not known yet. */
export const percentDone = (done: number, total: number): number =>
  total > 0 ? Math.min(100, Math.round((done / total) * 100)) : 0;

/** "CSV · Dhan · Aggregates" of the steps a job ran (all three: "All steps"). */
export function stepsText(steps: readonly string[]): string {
  const known = phaseKeys.filter((key) => steps.includes(key));
  if (known.length === phaseKeys.length) return 'All steps';
  return known.map((key) => stepNames[key]).join(' · ');
}
