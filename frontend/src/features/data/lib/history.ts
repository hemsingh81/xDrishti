import { ApiError } from '@/shared/api';
import type { ValidationProblem } from '@/shared/api';

const codeOf = (error: unknown): string | undefined => (error instanceof ApiError ? error.problem?.code : undefined);

export const statusLabels: Readonly<Record<string, string>> = {
  none: 'Not started',
  planned: 'Planned',
  partial: 'Partial',
  done: 'Done',
  seam_unresolved: 'Seam unresolved',
  seam_jump: 'Seam jump',
};

export const statusLabel = (status: string): string => statusLabels[status] ?? status;

export const probeLabel = (status: string): string =>
  status === 'passed' ? 'Passed' : status === 'unresolved' ? 'Unresolved' : 'Not probed';

const refusals: Readonly<Record<string, string>> = {
  'history.disabled': 'The 1-minute backfill is switched off, so nothing was started and nothing was fetched.',
  'history.outside_window': 'It can only run inside its time window.',
  'history.run_active': 'A backfill or a probe is already running. Wait for it to finish.',
  'history.calendar_missing': 'The trading calendar has gaps in the range to fetch, so the backfill was not started.',
  'history.disk_low': 'There is not enough free disk for the bars to fetch, so the backfill was not started.',
  'history.no_target': 'There is no instrument to fetch history for.',
  'data.dhan_not_connected': 'Dhan is not connected for the Data account. Reconnect it in Accounts.',
  'data.dhan_not_available': 'The Dhan Data API fetch is not available.',
  'auth.invalid_credentials': 'That password is not right.',
  'auth.step_up_required': 'Enter your password to confirm this act.',
  'auth.locked': 'Too many wrong passwords. Sign-in is locked for a while.',
};

/** The words for a refused or failed act: what the refusal means, then the server's own detail (it names the numbers and the window). */
export function actErrorText(error: unknown): string {
  const code = codeOf(error);
  const lead = code ? refusals[code] : undefined;
  const detail = error instanceof ApiError ? error.problem?.detail : undefined;
  if (lead) return detail && code !== 'auth.invalid_credentials' ? `${lead} ${detail}` : lead;
  if (error instanceof ApiError) {
    return error.status === 429
      ? 'Too many tries. Wait a moment and try again.'
      : (detail ?? error.problem?.title ?? 'The act could not be done.');
  }
  return 'Could not reach the server. Check your connection and try again.';
}

/** The same error with the words above as its detail, which is what the password dialog shows. */
export function withActText(error: unknown): unknown {
  if (!(error instanceof ApiError)) return error;
  const problem: ValidationProblem = { ...error.problem, detail: actErrorText(error) };
  return new ApiError(error.status, problem);
}
