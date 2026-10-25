import { ApiError } from '@/shared/api';
import { decisionError } from './planText';

/** What a failed owner act means, in words; the snooze and closed alert codes first, then the shared wording. */
export function alertError(error: unknown): string {
  const code = error instanceof ApiError ? error.problem?.code : undefined;
  if (code === 'plans.snooze_limit') return 'This alert cannot be snoozed again: the snooze limit is reached.';
  if (code === 'plans.alert_closed') return 'This alert is already closed. The screen shows its current state.';
  return decisionError(error);
}
