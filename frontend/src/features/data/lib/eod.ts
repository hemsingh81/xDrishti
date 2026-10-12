import type { DataJob, EodOverview, EodRun } from '@/shared/api';
import { formatIstDayAndTime } from '@/shared/lib/format';

// "Tue 13 Oct": the day of the next run in India time.
const istWeekdayDay = new Intl.DateTimeFormat('en-IN', {
  timeZone: 'Asia/Kolkata',
  weekday: 'short',
  day: 'numeric',
  month: 'short',
});

/** "Auto 15:50 · next Tue 13 Oct" (the next run is left out when none is planned); "Auto off" when switched off. */
export function autoLabel(eod: Pick<EodOverview, 'enabled' | 'timeIst' | 'nextRunAt'>): string {
  if (!eod.enabled) return 'Auto off';
  if (!eod.nextRunAt) return `Auto ${eod.timeIst}`;
  const parts = istWeekdayDay.formatToParts(new Date(eod.nextRunAt));
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? '';
  return `Auto ${eod.timeIst} · next ${part('weekday')} ${part('day')} ${part('month')}`;
}

export type EodTone = 'success' | 'error' | 'warning' | 'neutral';

export interface LastRunLook {
  tone: EodTone;
  /** "succeeded" / "failed" / "skipped" / "is running": said in words, never by colour alone. */
  word: string;
  /** When it ended (started, while it runs), "12 Oct 15:52". */
  at: string;
  /** The moment as an ISO string for `<time dateTime>`. */
  iso: string;
}

const words: Readonly<Record<string, { tone: EodTone; word: string }>> = {
  succeeded: { tone: 'success', word: 'succeeded' },
  failed: { tone: 'error', word: 'failed' },
  skipped: { tone: 'warning', word: 'skipped' },
  running: { tone: 'neutral', word: 'is running' },
};

/** How the latest automatic run is described: tone, word and time. */
export function lastRunLook(run: EodRun): LastRunLook {
  const known = words[run.status] ?? { tone: 'neutral' as const, word: run.status };
  const iso = run.finishedAt ?? run.startedAt;
  return { ...known, at: formatIstDayAndTime(iso, { weekday: false }), iso };
}

/** The sentence under the line for a run that did not succeed: its reason, else where to look. */
export function lastRunReason(run: EodRun): string | null {
  if (run.status === 'failed') return run.message ?? 'The reason is in Recent runs.';
  if (run.status === 'skipped') return run.message ?? 'No reason was recorded.';
  return null;
}

/** "Manual" or "Automatic": who started a run. */
export const triggerLabel = (job: Pick<DataJob, 'trigger'>): string =>
  job.trigger === 'daily' ? 'Automatic' : 'Manual';
