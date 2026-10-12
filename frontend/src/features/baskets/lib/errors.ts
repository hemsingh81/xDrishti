import { ApiError, type AddMembersResult } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

/**
 * An add that went out in several calls and failed after earlier calls had been applied (each call is all or nothing, so
 * those stay). Carries what was applied and the failure that stopped it.
 */
export class PartialAddError extends Error {
  constructor(
    readonly applied: AddMembersResult,
    readonly reason: unknown,
  ) {
    super('Only part of the instruments were added.');
    this.name = 'PartialAddError';
  }
}

/** The failure itself, whether or not part of the work was applied before it. */
const reasonOf = (error: unknown): unknown => (error instanceof PartialAddError ? error.reason : error);

/** 409 `baskets.conflict`: the basket changed under the request and every retry lost. Reload and try again. */
export const isConflict = (error: unknown): boolean => {
  const reason = reasonOf(error);
  return reason instanceof ApiError && reason.status === 409 && reason.problem?.code === 'baskets.conflict';
};

/**
 * A refused order: the list sent no longer holds every member / basket exactly once, because the basket changed after
 * the screen loaded (400 `baskets.invalid`). On a reorder this is a lost race, not a typing mistake.
 */
export const isStaleOrder = (error: unknown): boolean => {
  const reason = reasonOf(error);
  return reason instanceof ApiError && reason.status === 400 && reason.problem?.code === 'baskets.invalid';
};

/** The text for a failed basket call, in plain words (the API's problem title, or what it says about the ids). */
export function basketProblemText(error: unknown, fallback: string): string {
  const reason = reasonOf(error);
  if (!(reason instanceof ApiError)) return 'Could not reach the server. Check your connection and try again.';
  if (isConflict(reason)) {
    return 'The basket was changed somewhere else at the same moment. It has been reloaded — please try again.';
  }
  // Retired instruments are named, and too many members says how many, in `errors.instrumentIds`.
  return reason.problem?.errors?.instrumentIds?.[0] ?? reason.problem?.title ?? fallback;
}

/** The text for a refused reorder: a basket that changed meanwhile is reloaded (the caller does it), else as any call. */
export function reorderProblemText(error: unknown, fallback: string): string {
  return isStaleOrder(error) ? 'The basket changed; it has been reloaded.' : basketProblemText(error, fallback);
}

/** What stayed applied when an add stopped half way ("500 added to MyChoice"); '' when nothing was. */
export function appliedText(applied: readonly { basket: string; added: number }[]): string {
  const done = applied.filter((a) => a.added > 0).map((a) => `${formatNumber(a.added)} added to ${a.basket}`);
  return done.length > 0 ? `Before the error ${done.join(' and ')}; those stay.` : '';
}

/** Per-field messages (`name`, `colour`, `purpose`, `description`) of a `baskets.invalid` / `baskets.name_taken` answer. */
export function basketFieldErrors(error: unknown): Readonly<Record<string, string>> {
  const errors = error instanceof ApiError ? error.problem?.errors : undefined;
  if (!errors) return {};
  return Object.fromEntries(Object.entries(errors).map(([field, messages]) => [field, messages.join(' ')]));
}
