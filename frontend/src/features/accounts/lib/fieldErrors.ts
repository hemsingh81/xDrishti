import { isStepUpCancelled } from '@/features/auth';
import { ApiError } from '@/shared/api';

/** First-class per-field messages keyed by the API's field path (e.g. `instruments.intraday`). */
export type FieldErrors = Readonly<Record<string, string>>;

export const noErrors: FieldErrors = {};

/** The per-field messages of an `accounts.invalid` problem (several messages for a field are joined). */
export function fieldErrorsOf(error: unknown): FieldErrors {
  const errors = error instanceof ApiError ? error.problem?.errors : undefined;
  if (!errors) {
    return noErrors;
  }
  return Object.fromEntries(Object.entries(errors).map(([field, messages]) => [field, messages.join(' ')]));
}

/**
 * Paths in create requests carry the section ("trading.riskProfileId"); the section's own form uses the bare field.
 * Returns the errors with `section.` stripped, so one form works for both the tab and the wizard.
 */
export function scoped(errors: FieldErrors, section: string): FieldErrors {
  const prefix = `${section}.`;
  return Object.fromEntries(
    Object.entries(errors).map(([field, message]) => [
      field.startsWith(prefix) ? field.slice(prefix.length) : field,
      message,
    ]),
  );
}

/** Every field message as a list (for screens that have no per-field place for them). */
export function allMessages(errors: FieldErrors): string[] {
  return Object.values(errors);
}

/**
 * The text for a failed request that is not about a single field (conflict, not found, outage…).
 * Undefined when there is nothing to say: no error, a cancelled password dialog, or per-field messages.
 */
export function problemMessage(error: unknown): string | undefined {
  if (!error || isStepUpCancelled(error)) {
    return undefined;
  }
  if (!(error instanceof ApiError)) {
    return 'Could not reach the server. Check your connection and try again.';
  }
  if (Object.keys(fieldErrorsOf(error)).length > 0) {
    return undefined;
  }
  if (error.problem?.code === 'auth.step_up_required') {
    return 'Your password confirmation is needed for this change. Try again.';
  }
  return error.problem?.title ?? 'The change could not be saved. Please try again.';
}

/**
 * The messages of fields that the form has no place for (an API field the form does not show), joined; undefined when
 * every message belongs to a field the form shows. Keeps a refusal from being swallowed.
 */
export function unplacedMessages(errors: FieldErrors, shown: readonly string[]): string | undefined {
  const rest = Object.entries(errors)
    .filter(([field]) => !shown.includes(field))
    .map(([field, message]) => `${field}: ${message}`);
  return rest.length > 0 ? rest.join(' ') : undefined;
}
