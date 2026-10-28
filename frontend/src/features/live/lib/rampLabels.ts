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
  'The step goes up only when every evidence gate G1 to G6 passes; a gate that cannot be read never passes. The system lowers or holds the step by itself when the evidence turns. Gates at this size are a safety brake on gross problems, not proof of an edge.';

export const gateNames: Record<string, string> = {
  G1_sessions: 'G1 Sessions at this step',
  G2_volume: 'G2 Graded round trips',
  G3_slippage: 'G3 Slippage against the plan',
  G4_discipline: 'G4 Discipline',
  G5_containment: 'G5 Containment',
  G6_capture: 'G6 Capture',
  G7_cost_drag: 'G7 Cost drag (information)',
};

/** State word and a symbol: the state is never carried by colour alone. */
export const gateStates: Record<string, { word: string; icon: string }> = {
  pass: { word: 'Pass', icon: '✔' },
  fail: { word: 'Fail', icon: '✖' },
  insufficient: { word: 'Not enough evidence', icon: '…' },
  unavailable: { word: 'Cannot be read', icon: '?' },
  not_evaluable: { word: 'Cannot be evaluated', icon: '?' },
  info: { word: 'Information', icon: 'i' },
};

export const measureStates: Record<string, string> = {
  value: 'measured',
  realised_only: 'realised only, open positions not marked',
  unavailable: 'unavailable',
};

export const haltActions: Record<string, string> = {
  stop_new: 'no new entries',
  halve: 'size halves next week',
  alert_only: 'alert only',
};

/** A number with the given decimals, or a word when it is not known (never a made-up digit). */
export function num(value: number | null | undefined, digits = 3): string {
  return typeof value === 'number' ? value.toFixed(digits) : 'not known';
}

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
