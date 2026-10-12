import { ApiError } from '@/shared/api';

/** Plain-words text for a failed request (the API's problem title when it sent one). */
export function problemText(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    return error.problem?.title ?? fallback;
  }
  return 'Could not reach the server. Check your connection and try again.';
}

const codeOf = (error: unknown): string | undefined => (error instanceof ApiError ? error.problem?.code : undefined);

/** 409 `data.job_running`: an update is already running; the page just watches it. */
export const isJobRunning = (error: unknown): boolean =>
  error instanceof ApiError && codeOf(error) === 'data.job_running';

/** The sentence for a start that did not work. */
export function startErrorText(error: unknown): string {
  switch (codeOf(error)) {
    case 'data.master_missing':
      return 'The instrument list is missing. Refresh it first on the Instruments page, then update the data.';
    case 'data.job_running':
      return 'An update is already running.';
    default:
      return problemText(error, 'The update could not be started.');
  }
}

const codeText: Readonly<Record<string, string>> = {
  'data.csv_folder_missing': 'The CSV folder was not found.',
  'data.dhan_not_connected': 'Dhan is not connected for the Data account. Reconnect it in Accounts.',
  'data.dhan_not_available': 'The Dhan Data API fetch is not available.',
};

/** What went wrong with a phase, in words: the phase's own message, else the meaning of its code. */
export const phaseReason = (message: string | null, code: string | null): string =>
  message ?? (code ? (codeText[code] ?? 'The step did not finish.') : 'The step did not finish.');
