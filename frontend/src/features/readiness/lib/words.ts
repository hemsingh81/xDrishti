// The words the Readiness screen prints for the server's codes (CF-11). An unknown code is printed as the server sent it
// and is never read as a pass: only the exact code `pass` is one.

export const checklistSize = 15;

const score = new Intl.NumberFormat('en-IN', { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** 20.17 -> "20.2": a 0 to 100 score at one decimal (the spec's own examples read 20.2 and 60.7). */
export const formatScore = (value: number): string => score.format(value);

export type RowStatus = 'pass' | 'fail' | 'unavailable';

/** Anything but `pass` and `fail` is unavailable: an unknown status is never a pass. */
export const rowStatus = (status: string): RowStatus =>
  status === 'pass' || status === 'fail' ? status : 'unavailable';

export const rowStatusWords: Record<RowStatus, string> = {
  pass: 'Pass',
  fail: 'Fail',
  unavailable: 'Not available (counts as a fail)',
};

export type LabelKind = 'not_ready' | 'building' | 'usable' | 'ready' | 'other';

export const labelKind = (code: string): LabelKind =>
  code === 'not_ready' || code === 'building' || code === 'usable' || code === 'ready' ? code : 'other';

export const subScoreStateWords = (state: string): string => {
  switch (state) {
    case 'scored':
      return 'Scored';
    case 'not_applicable':
      return 'Not applicable (left out)';
    case 'unavailable':
      return 'Not available (a hard fail)';
    default:
      return state.replaceAll('_', ' ');
  }
};

/** What the enforce switch means in each position. */
export const enforceMeaning = (on: boolean): string =>
  on
    ? "ON: a readiness gate that fails refuses the move it judges with 409 confidence.gate_failed and the failing rows. The moves are Candidate to Paper, Paper to Active and Paused to Paper (the owner's re-enable). A row that is not available blocks the move."
    : 'OFF (the default): the gates are still evaluated and the result with the failing rows is recorded on the changelog row, but the move goes ahead with a warning. Candidate to Paper, Paper to Active and Paused to Paper are not refused.';

/** True when the whole list reads 0, the state to explain in plain words. */
export const allZero = (scores: readonly number[]): boolean => scores.length > 0 && scores.every((s) => s === 0);
