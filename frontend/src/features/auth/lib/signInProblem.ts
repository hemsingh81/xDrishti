import { ApiError } from '@/shared/api';

const fallbackRetrySeconds = 60;

/** What went wrong with a sign-in attempt, in the terms the page needs to show. */
export type SignInProblem =
  | { kind: 'invalid_credentials' }
  | { kind: 'locked'; retryAfterSeconds: number }
  | { kind: 'rate_limited'; retryAfterSeconds: number }
  | { kind: 'unavailable' };

export function describeSignInProblem(error: unknown): SignInProblem {
  if (!(error instanceof ApiError)) {
    return { kind: 'unavailable' };
  }
  const retryAfterSeconds = Math.max(1, error.problem?.retryAfterSeconds ?? fallbackRetrySeconds);
  switch (error.problem?.code ?? statusCode(error.status)) {
    case 'auth.invalid_credentials':
      return { kind: 'invalid_credentials' };
    case 'auth.locked':
      return { kind: 'locked', retryAfterSeconds };
    case 'auth.rate_limited':
      return { kind: 'rate_limited', retryAfterSeconds };
    default:
      return { kind: 'unavailable' };
  }
}

// Fallback when a proxy replaced the problem body.
function statusCode(status: number): string | undefined {
  return { 401: 'auth.invalid_credentials', 423: 'auth.locked', 429: 'auth.rate_limited' }[status];
}
