import { ApiError, type ServiceRun } from '@/shared/api';
import { problemText } from './errors';

export interface RefreshMessage {
  text: string;
  tone: 'success' | 'info' | 'error';
}

/** The toast for a quote run that ended: the run's own message ("Quoted 41 of 43 holdings; 2 not on NSE") on success. */
export function refreshOutcome(run: ServiceRun | null): RefreshMessage {
  const detail = run?.message ? `: ${run.message}` : '.';
  switch (run?.status) {
    case 'failed':
      return { text: `Prices could not be refreshed${detail}`, tone: 'error' };
    case 'skipped':
      return { text: `Prices were not refreshed${detail}`, tone: 'info' };
    default:
      return { text: run?.message ?? 'Prices refreshed.', tone: 'success' };
  }
}

/** A refused or failed start: 409 `services.already_running` is explained and the run going on is followed. */
export function refreshRefusal(error: unknown): RefreshMessage & { following: boolean } {
  if (error instanceof ApiError && error.problem?.code === 'services.already_running') {
    return {
      text: 'Prices are already being refreshed. This page updates when that run ends.',
      tone: 'info',
      following: true,
    };
  }
  return { text: problemText(error, 'Prices could not be refreshed.'), tone: 'error', following: false };
}
