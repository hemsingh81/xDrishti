import { formatDate } from './format';

/** The health of one sync (holdings, trades or ledger) as the API reports it (ADR 0027). Every field may be missing in an old answer. */
export interface SyncHealth {
  /** Holdings: failed attempts of the day of the latest failure. Trades and ledger: the number of the latest run within the day. */
  attempts?: number;
  /** Days in a row whose last attempt failed; 0 when the latest succeeded. */
  consecutiveFailedDays?: number;
  /** A reason code (`unavailable`, `rate_limited`, …); null while healthy. */
  lastFailureReason?: string | null;
  /** When the next quiet retry is due; null when none is pending. */
  retryAt?: string | null;
}

/** A failed occurrence is retried quietly this many times (1, 5 and 15 minutes later); the number the owner sees is "k of 3". */
export const maxQuietRetries = 3;

const reasons: Readonly<Record<string, string>> = {
  unavailable: 'Dhan could not be reached',
  rate_limited: 'Dhan asked for fewer requests',
  interrupted: 'the run was interrupted',
  rejected: 'Dhan rejected the request',
  history_too_large: 'the history was too large to read at once',
  token_expired: 'the Dhan login has expired',
  token_refused: 'Dhan refused the login',
  not_connected: 'the account is not connected to Dhan',
  data_changed: 'the data changed while it was being saved; it will be tried again',
  unexpected: 'something unexpected went wrong',
};

/** The reason code of a failed sync in plain words ("Dhan could not be reached"); a code this version does not know is "an unknown problem". */
export const failureReasonText = (code: string | null | undefined): string =>
  (code ? reasons[code] : undefined) ?? 'an unknown problem';

const plural = (n: number, one: string, many: string) => `${String(n)} ${n === 1 ? one : many}`;

/**
 * "retrying in 5 minutes (attempt 2 of 3)" while a quiet retry is pending; null when none is. A time that has come is
 * "retrying now". Nothing here says the sync is broken: it is what the owner sees while the app deals with it.
 */
export function retryText(health: SyncHealth, now: number = Date.now()): string | null {
  if (!health.retryAt) return null;
  const minutes = Math.ceil((Date.parse(health.retryAt) - now) / 60_000);
  const attempt = Math.min(Math.max(health.attempts ?? 1, 1), maxQuietRetries);
  const when = minutes <= 0 ? 'retrying now' : `retrying in ${plural(minutes, 'minute', 'minutes')}`;
  return `${when} (attempt ${String(attempt)} of ${String(maxQuietRetries)})`;
}

/** "failed for 2 days: Dhan could not be reached"; null while healthy or while a retry is still to come (see `retryText`). */
export function failingText(health: SyncHealth): string | null {
  const days = health.consecutiveFailedDays ?? 0;
  if (days < 1 || health.retryAt) return null;
  return `failed for ${plural(days, 'day', 'days')}: ${failureReasonText(health.lastFailureReason)}`;
}

/** The one line about the health of a sync: the quiet retry if one is due, else how long it has been failing; null when all is well. */
export function healthText(health: SyncHealth, now: number = Date.now()): string | null {
  return retryText(health, now) ?? failingText(health);
}

/** How far the nightly deepening read the trade history back ("History read back to 3 Mar 2023"), or that there is no more ("No earlier history found"); null before it began. */
export function deepenText(deepenedTo: string | null | undefined, exhausted: boolean | undefined): string | null {
  if (deepenedTo && exhausted) return `History read back to ${formatDate(deepenedTo)} — no earlier history found`;
  if (deepenedTo) return `History read back to ${formatDate(deepenedTo)}`;
  return exhausted ? 'No earlier history found' : null;
}
