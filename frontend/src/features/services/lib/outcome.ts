import type { ServiceView } from '@/shared/api';

export interface RunOutcome {
  text: string;
  tone: 'success' | 'error' | 'info';
}

/** The sentence for the toast when a service that was running is not any more: how its latest run ended. */
export function outcomeOf(service: ServiceView): RunOutcome {
  const run = service.lastRun;
  const message = run?.message ? `: ${run.message}` : '.';
  switch (run?.status) {
    case 'failed':
      return { text: `${service.name} failed${message}`, tone: 'error' };
    case 'skipped':
      return { text: `${service.name} was skipped${message}`, tone: 'info' };
    default:
      return { text: `${service.name} finished${message}`, tone: 'success' };
  }
}

export const isRunning = (service: ServiceView): boolean => service.status === 'running';

/** The earliest next run of an enabled service (ISO), to read the list again just after it: a scheduled start is noticed. */
export function earliestNextRun(services: readonly ServiceView[]): string | null {
  const times = services.flatMap((s) => (s.enabled && s.nextRun ? [s.nextRun] : []));
  return times.length === 0 ? null : times.reduce((a, b) => (Date.parse(a) <= Date.parse(b) ? a : b));
}
