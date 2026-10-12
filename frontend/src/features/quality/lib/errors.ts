import { ApiError } from '@/shared/api';

/** Plain-words text for a failed request (the API's problem title when it sent one). */
export function problemText(error: unknown, fallback: string): string {
  if (error instanceof ApiError) return error.problem?.title ?? fallback;
  return 'Could not reach the server. Check your connection and try again.';
}

const codeOf = (error: unknown): string | undefined => (error instanceof ApiError ? error.problem?.code : undefined);

/** The sentence for a start (Run checks, Re-fetch, Refresh aggregates) that did not work. */
export function startErrorText(error: unknown): string {
  switch (codeOf(error)) {
    case 'data.job_running':
      return 'An update or a check is already running. It finishes first; this page shows its progress.';
    case 'data.instrument_not_found':
      return 'That instrument no longer exists.';
    case 'data.dhan_not_connected':
      return 'Dhan is not connected for the Data account. Reconnect it in Accounts, then re-fetch.';
    case 'data.dhan_not_available':
      return 'Dhan cannot be asked for this instrument, so it cannot be re-fetched.';
    default:
      return problemText(error, 'The job could not be started.');
  }
}
