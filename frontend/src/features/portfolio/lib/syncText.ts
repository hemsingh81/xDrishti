import type { SyncRun } from '@/shared/api';

/** What a finished run changed, in a few words ("2 new, 1 closed", "no changes"). Zero counts are left out. */
export function runCounts(run: SyncRun): string {
  if (run.trigger === 'import') return `${String(run.newCount)} imported`;
  const parts = [
    run.newCount > 0 ? `${String(run.newCount)} new` : null,
    run.increasedCount > 0 ? `${String(run.increasedCount)} increased` : null,
    run.decreasedCount > 0 ? `${String(run.decreasedCount)} decreased` : null,
    run.closedCount > 0 ? `${String(run.closedCount)} closed` : null,
  ].filter((part): part is string => part !== null);
  if (parts.length > 0) return parts.join(', ');
  return run.unchangedCount > 0 ? 'no changes' : 'nothing to change';
}

export type RunTone = 'success' | 'error' | 'warning' | 'info';

export interface RunSummary {
  tone: RunTone;
  /** "Synced", "Imported", "Sync failed", "Skipped", "Syncing". */
  title: string;
  /** The detail after the title: counts, or the reason in plain words. */
  detail: string;
}

const triggerWord = { manual: 'Synced', auto: 'Auto-synced', import: 'Imported' } as const;

/** A run as one line: what happened and, for a failed or skipped run, why (the API's short safe text). */
export function summariseRun(run: SyncRun): RunSummary {
  if (run.status === 'succeeded') {
    const word = triggerWord[run.trigger as keyof typeof triggerWord] as string | undefined;
    return { tone: 'success', title: word ?? 'Synced', detail: runCounts(run) };
  }
  if (run.status === 'failed') {
    return { tone: 'error', title: 'Sync failed', detail: run.error ?? 'Nothing was changed.' };
  }
  if (run.status === 'skipped') {
    return { tone: 'warning', title: 'Skipped', detail: run.error ?? 'The automatic sync did not run.' };
  }
  return { tone: 'info', title: 'Syncing', detail: 'in progress' };
}

interface Schedule {
  mode: string;
  dailyTimeIst: string;
}

/** The auto refresh setting in words: off or once a day. */
export function describeSchedule(schedule: Schedule): string {
  return schedule.mode === 'daily' ? `Auto refresh: daily at ${schedule.dailyTimeIst} IST` : 'Auto refresh: off';
}
