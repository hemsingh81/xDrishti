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

/** Per-field messages of a 400 `data.invalid` answer (`date`, `kind`, `exDate`, `numerator`, `note`, …), one text per field. */
export function fieldErrors(error: unknown): Readonly<Record<string, string>> {
  const errors = error instanceof ApiError ? error.problem?.errors : undefined;
  if (!errors) return {};
  return Object.fromEntries(Object.entries(errors).map(([field, messages]) => [field, messages.join(' ')]));
}

/** The sentence for a failed calendar change; a day that is gone (404) says so, the list has been reloaded. */
export function calendarErrorText(error: unknown, fallback: string): string {
  if (codeOf(error) === 'data.calendar_day_not_found')
    return 'That day is no longer in the calendar. The list was reloaded.';
  return problemText(error, fallback);
}

/** 409 `data.csv_already_adjusted`: the ex-date is inside CSV history that is adjusted at the source already. */
export const isCsvAlreadyAdjusted = (error: unknown): boolean => codeOf(error) === 'data.csv_already_adjusted';

/** A whole-number extension member of a problem (the API adds some next to `code`); null when it is not there. */
function problemCount(error: unknown, member: string): number | null {
  if (!(error instanceof ApiError) || !error.problem) return null;
  const members: Readonly<Record<string, unknown>> = { ...error.problem };
  const value = members[member];
  return typeof value === 'number' && Number.isInteger(value) ? value : null;
}

/** How many actions (`adjustsCsvTwice`) on how many instruments (`instruments`) a 409 `data.csv_already_adjusted` is about. */
export const csvAdjustedCounts = (error: unknown): { actions: number | null; instruments: number | null } => ({
  actions: problemCount(error, 'adjustsCsvTwice'),
  instruments: problemCount(error, 'instruments'),
});

const pluralOf = (count: number, one: string, many: string) => `${String(count)} ${count === 1 ? one : many}`;

function csvAdjustedText(error: unknown): string {
  const { actions, instruments } = csvAdjustedCounts(error);
  const what =
    actions !== null && instruments !== null
      ? `${pluralOf(actions, 'action', 'actions')} on ${pluralOf(instruments, 'instrument', 'instruments')}`
      : 'Some actions';
  return `${what} would adjust history that is already adjusted. Confirm that you want this, or leave it out.`;
}

/** The sentence for a failed corporate action change; a taken-back or vanished action and a repeat say so in words. */
export function actionErrorText(error: unknown, fallback: string): string {
  switch (codeOf(error)) {
    case 'data.corporate_action_conflicts_same_day':
      return 'Another action of the other kind exists on that date for this instrument — ignore it first.';
    case 'data.csv_already_adjusted':
      return csvAdjustedText(error);
    case 'data.corporate_action_exists':
      return 'This instrument already has an action of this kind on that ex-date. Edit that one instead.';
    case 'data.corporate_action_not_found':
      return 'That action is no longer in the list. The list was reloaded.';
    case 'data.instrument_not_found':
      return 'That instrument is not in the instrument list.';
    default:
      return problemText(error, fallback);
  }
}

/** The sentence for a failed CSV review request (list, preview, undo): a vanished file and a running job say so in words. */
export function csvErrorText(error: unknown, fallback: string): string {
  switch (codeOf(error)) {
    case 'data.csv_file_not_found':
      return 'That file was never loaded, or is no longer in the folder. The list was reloaded.';
    case 'data.job_running':
      return 'An update is already running. Wait for it to finish.';
    case 'data.invalid':
      return Object.values(fieldErrors(error)).join(' ') || problemText(error, fallback);
    default:
      return problemText(error, fallback);
  }
}
