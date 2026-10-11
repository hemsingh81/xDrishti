import { Alert } from '@mui/material';
import type { SignInProblem } from '../lib/signInProblem';
import { RetryCountdown } from './RetryCountdown';

/** Inline error for a failed sign-in: icon + text (never colour alone). Locked/rate-limited show a live countdown. */
export function SignInProblemAlert({ problem, onElapsed }: { problem: SignInProblem; onElapsed: () => void }) {
  switch (problem.kind) {
    case 'invalid_credentials':
      return <Alert severity="error">The email or password is incorrect.</Alert>;
    case 'locked':
      return (
        <Alert severity="warning">
          Too many failed attempts, so sign-in is locked. Try again in{' '}
          <RetryCountdown seconds={problem.retryAfterSeconds} onElapsed={onElapsed} />.
        </Alert>
      );
    case 'rate_limited':
      return (
        <Alert severity="warning">
          Too many sign-in attempts. Try again in{' '}
          <RetryCountdown seconds={problem.retryAfterSeconds} onElapsed={onElapsed} />.
        </Alert>
      );
    case 'unavailable':
      return <Alert severity="error">Sign-in is not available right now. Check your connection and try again.</Alert>;
  }
}
