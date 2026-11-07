import { ApiError } from '@/shared/api';
import { formatDuration } from '@/shared/lib/format';

/**
 * The words of a refused or failed ML act (switch change or rollback apply). The server's sentence is shown as sent, as text, with
 * its code; a wrong password is an answer (the owner stays signed in). Nothing was changed unless the server said it succeeded.
 */
export function mlProblem(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was changed.';
  const p = error.problem;
  const code = p?.code ? ` (${p.code})` : '';
  const server = `${[p?.title, p?.detail].filter(Boolean).join(' ')}${code}`.trim();
  switch (error.status) {
    case 400:
    case 422:
      return `${server || 'The request was not valid.'} Nothing was changed.`;
    case 401:
      // A 401 on an act that asks no password (any OFF, an apply) means the session ended, not a wrong password.
      return p?.code === 'auth.invalid_credentials'
        ? 'The password was not accepted. Nothing was changed.'
        : 'Your session has ended. Nothing was changed.';
    case 403:
      return p?.code === 'auth.step_up_required'
        ? `This change needs your password again. Nothing was changed. ${server}`.trim()
        : `You are not allowed to do this. Nothing was changed. ${server}`.trim();
    case 404:
      return `It was not found; it may be gone. Nothing was changed. ${server}`.trim();
    case 409:
      return `Refused: ${server || 'the record is closed.'} Nothing was changed.`;
    case 423:
      return `Too many wrong passwords; sign-in is locked for now. ${server}`.trim();
    case 429: {
      const wait = p?.retryAfterSeconds;
      return `${server || 'Limit reached.'}${wait ? ` Try again in ${formatDuration(wait)}.` : ''}`.trim();
    }
    default:
      return `${server || 'The request failed.'} Nothing was changed.`;
  }
}
