import { ApiError } from '@/shared/api';

export type ProblemKind =
  | 'not_connected'
  | 'role_off'
  | 'no_bucket'
  | 'sync_running'
  | 'confirmation_required'
  | 'confirmation_stale'
  | 'conflict'
  | 'invalid'
  | 'not_found'
  | 'alert_limit'
  | 'preview_stale'
  | 'lot_from_trade'
  | 'broker_error'
  | 'network'
  | 'other';

const byCode: Record<string, ProblemKind> = {
  'portfolio.not_connected': 'not_connected',
  'portfolio.role_off': 'role_off',
  'portfolio.no_bucket': 'no_bucket',
  'portfolio.sync_running': 'sync_running',
  'portfolio.confirmation_required': 'confirmation_required',
  'portfolio.confirmation_stale': 'confirmation_stale',
  'portfolio.conflict': 'conflict',
  'portfolio.invalid': 'invalid',
  'portfolio.not_found': 'not_found',
  'portfolio.alert_limit': 'alert_limit',
  'portfolio.preview_stale': 'preview_stale',
  'portfolio.lot_from_trade': 'lot_from_trade',
  'accounts.broker_error': 'broker_error',
};

export function problemKind(error: unknown): ProblemKind {
  if (!(error instanceof ApiError)) return 'network';
  const code = error.problem?.code;
  return (code ? byCode[code] : undefined) ?? 'other';
}

const friendly: Partial<Record<ProblemKind, string>> = {
  not_connected: 'Connect the account first. Dhan is not signed in for it (or the sign-in has expired).',
  role_off: 'Switch on the Portfolio role of this account first.',
  no_bucket:
    'This account has no portfolio bucket to put holdings in. Switch its Portfolio role off and on again in Accounts.',
  sync_running: 'A sync or import of this account is already running. Wait a moment for it to finish.',
  conflict: 'The lot was changed or closed meanwhile. Reload and try again.',
  preview_stale: 'Holdings or trades changed — please review again.',
  lot_from_trade:
    'This lot comes from your trades, so its date follows them. Change the trades and rebuild the lots instead.',
  alert_limit: 'You have reached the limit of 200 active alerts. Cancel one you no longer need, then set this one.',
  broker_error: 'Dhan could not be reached right now. Try again in a moment.',
  network: 'Could not reach the server. Check your connection and try again.',
};

/** Plain-words text for a failed request: a friendly line for the known cases, else the API's title, else `fallback`. */
export function problemText(error: unknown, fallback: string): string {
  const kind = problemKind(error);
  return friendly[kind] ?? (error instanceof ApiError ? (error.problem?.title ?? fallback) : fallback);
}

export interface Confirmation {
  /** How many holdings Dhan listed. */
  listedHoldings: number;
  /** How many the account has open. */
  openHoldings: number;
  /** How many of them the sync would close. */
  wouldClose: number;
  /** The numbers changed since the owner last looked: they must be reviewed again before confirming. */
  stale: boolean;
}

/**
 * The numbers of a `portfolio.confirmation_required` answer, or of a `portfolio.confirmation_stale` one (the fresh ones);
 * null for any other error.
 */
export function confirmationOf(error: unknown): Confirmation | null {
  const kind = problemKind(error);
  if ((kind !== 'confirmation_required' && kind !== 'confirmation_stale') || !(error instanceof ApiError)) return null;
  const { openHoldings, wouldClose, listedHoldings } = error.problem ?? {};
  if (typeof openHoldings !== 'number' || typeof wouldClose !== 'number') return null;
  return {
    listedHoldings: typeof listedHoldings === 'number' ? listedHoldings : Math.max(0, openHoldings - wouldClose),
    openHoldings,
    wouldClose,
    stale: kind === 'confirmation_stale',
  };
}

/** Messages per field of a 400 `portfolio.invalid` (keys as the API names them, e.g. `rows[2].acquiredOn`). */
export function fieldMessages(error: unknown): Record<string, string> {
  const errors = error instanceof ApiError ? error.problem?.errors : undefined;
  return errors ? Object.fromEntries(Object.entries(errors).map(([field, list]) => [field, list.join(' ')])) : {};
}

/**
 * The history call refuses a scope with too many bars (400 `portfolio.invalid`, field `scope`): the message of the field,
 * else null. The server's guard does not depend on the range, so neither a chip nor Retry clears it: the screen shows the
 * server's own words (which carry the advice) and no Retry.
 */
export function scopeTooLarge(error: unknown): string | null {
  return problemKind(error) === 'invalid' ? (fieldMessages(error).scope ?? null) : null;
}

/** Where to fix a connection problem: the account's tab in Accounts. */
export const accountTabPath = (accountId: string, tab: 'connection' | 'roles' | 'portfolio'): string =>
  `/accounts?account=${accountId}&tab=${tab}`;
