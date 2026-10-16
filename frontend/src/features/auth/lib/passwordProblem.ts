import { ApiError } from '@/shared/api';
import { describeSignInProblem, type SignInProblem } from './signInProblem';

/** What went wrong with a password change, in the terms the form needs to show. */
export type PasswordProblem =
  | { kind: 'wrong_current' }
  | { kind: 'new_password'; message: string }
  | { kind: 'locked'; problem: Extract<SignInProblem, { kind: 'locked' | 'rate_limited' }> }
  | { kind: 'busy' }
  | { kind: 'failed' };

export function describePasswordProblem(error: unknown): PasswordProblem {
  if (!(error instanceof ApiError)) {
    return { kind: 'failed' };
  }
  const code = error.problem?.code;
  if (code === 'auth.password_invalid' || code === 'auth.password_unchanged') {
    const message = error.problem?.errors?.newPassword?.join(' ') ?? error.problem?.title;
    return { kind: 'new_password', message: message ?? 'This password cannot be used.' };
  }
  if (code === 'auth.busy' || error.status === 409) {
    return { kind: 'busy' };
  }
  const signIn = describeSignInProblem(error);
  switch (signIn.kind) {
    case 'invalid_credentials':
      return { kind: 'wrong_current' };
    case 'locked':
    case 'rate_limited':
      return { kind: 'locked', problem: signIn };
    case 'unavailable':
      return { kind: 'failed' };
  }
}
