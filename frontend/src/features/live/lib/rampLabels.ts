import { ApiError } from '@/shared/api';

export const stepNames: Record<number, string> = { 1: 'S1 (25 % of the risk)', 2: 'S2 (50 %)', 3: 'S3 (100 %)' };

export const kindLabels: Record<string, string> = {
  set_step: 'Step set by you',
  auto_step_up: 'Raised by rule',
  hold: 'Hold by rule',
  clear_hold: 'Hold cleared by you',
  step_down: 'Lowered by rule',
  exit_confirmed: 'Exit confirmed by you',
};

export const noOrderSentence =
  'No setting lets this system place, change or cancel an order: you place every order yourself.';

export const gatesSentence =
  'The evidence gates are built in the next slice (P11-04b). Until they exist none can pass, so the step cannot be raised here; you can set S1 and lower the step.';

/** A percent with 3 decimals, or a word when the value is not known (never a made-up digit). */
export function pct(value: number | null | undefined): string {
  return typeof value === 'number' ? `${value.toFixed(3)} %` : 'not known';
}

/** What the server said, in words. A wrong password is an answer, not a sign-out. Nothing was saved in any of these cases. */
export function writeProblem(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was saved.';
  const p = error.problem;
  const server = [p?.title, p?.detail].filter(Boolean).join(' ');
  switch (error.status) {
    case 401:
      return 'The password was not accepted. Nothing was saved.';
    case 403:
      return p?.code === 'live.capability_off'
        ? 'Live writes are switched off in this deployment (Live__Writes__Enabled). Nothing was saved.'
        : `Not allowed. ${server}`.trim();
    case 409:
      return `${server} Nothing was saved.`.trim();
    case 422:
      return `${server} Nothing was saved.`.trim();
    case 423:
      return `Too many wrong passwords; sign-in is locked for now. ${server}`.trim();
    case 429:
      return server || 'Limit reached. Try again later.';
    default:
      return server || 'The request failed. Nothing was saved.';
  }
}
