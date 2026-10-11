import { isStepUpCancelled } from '@/features/auth';
import { ApiError } from '@/shared/api';
import { formatHoursMinutes } from '@/shared/lib/format';
import { fieldErrorsOf, problemMessage } from './fieldErrors';

/**
 * Friendly texts for the codes of connecting to a broker — from a failed request, or from the broker's return to
 * /accounts?connectError={code}. The code in the URL is never echoed: an unknown one gets the generic text.
 */
const messages: Readonly<Record<string, string>> = {
  'accounts.connect_expired':
    'The sign-in is no longer valid (it expired after 10 minutes, was already used, or was started in another browser session). Connect again.',
  'accounts.connect_cancelled': 'The sign-in at Dhan was cancelled. Nothing was changed.',
  'accounts.connect_mismatch':
    'You signed in to a different Dhan account than this one (its client id does not match). Nothing was stored — sign in with the right account.',
  'accounts.broker_error': 'Dhan did not answer as expected. Nothing was changed — try again in a few minutes.',
  'accounts.app_not_configured': 'Store the API key and secret (Broker app → Replace secrets) before connecting.',
  'accounts.not_found': 'This account no longer exists.',
  'accounts.conflict': 'The account changed while you were signing in. Connect again.',
  'auth.unauthenticated': 'Your xDrishti session had ended when Dhan sent you back. Sign in and connect again.',
};

const fallback = 'Connecting to Dhan failed. Try again.';

/** Own keys only: a code such as `__proto__` or `toString` from the URL must not reach inherited object members. */
function knownMessage(code: string | null | undefined): string | undefined {
  return code && Object.hasOwn(messages, code) ? messages[code] : undefined;
}

export function connectErrorMessage(code: string | null | undefined): string {
  return knownMessage(code) ?? fallback;
}

function consentLimitMessage(retryAfterSeconds: number | undefined): string {
  const wait = retryAfterSeconds && retryAfterSeconds > 0 ? `in ${formatHoursMinutes(retryAfterSeconds)}` : 'tomorrow';
  return `Dhan allows 25 sign-ins a day and today's are used up. Try again ${wait}.`;
}

/**
 * The text for a failed connection action (connect, disconnect). Undefined when there is nothing
 * to say: no error, a cancelled password dialog, or per-field messages (shown at the field).
 */
export function connectionProblem(error: unknown): string | undefined {
  if (!error || isStepUpCancelled(error)) {
    return undefined;
  }
  if (error instanceof ApiError && Object.keys(fieldErrorsOf(error)).length === 0) {
    const { code, title, retryAfterSeconds } = error.problem ?? {};
    if (code === 'accounts.consent_limit') {
      return consentLimitMessage(retryAfterSeconds);
    }
    if (code === 'accounts.broker_error' && title) {
      return title;
    }
    const known = knownMessage(code);
    if (known) {
      return known;
    }
  }
  return problemMessage(error);
}
