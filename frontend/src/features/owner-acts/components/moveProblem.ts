import { ApiError } from '@/shared/api';
import { formatDuration } from '@/shared/lib/format';

/**
 * The words of a refused or failed owner act. The server's sentence is shown as sent (as text) with its code; a wrong password is an answer here
 * (the owner stays signed in), and in every case nothing was changed unless the server said it succeeded.
 */
export function moveProblem(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was changed.';
  const p = error.problem;
  const server = [p?.title, p?.code ? `(${p.code})` : ''].filter(Boolean).join(' ');
  switch (error.status) {
    case 401:
      return 'The password was not accepted. Nothing was changed.';
    case 403:
      return `This act is switched off. ${server}`.trim();
    case 423:
      return `Too many wrong passwords; sign-in is locked for now. ${server}`.trim();
    case 429: {
      const wait = p?.retryAfterSeconds;
      return `${server || 'Limit reached.'}${wait ? ` Try again in ${formatDuration(wait)}.` : ''}`.trim();
    }
    default:
      return `${server || 'The request failed.'} Nothing was changed.`.trim();
  }
}
