import { ApiError } from '@/shared/api';

/** Code of the 403 the API answers when a sensitive change needs a fresh password check. */
export const stepUpRequiredCode = 'auth.step_up_required';

export function isStepUpRequired(error: unknown): boolean {
  return error instanceof ApiError && error.status === 403 && error.problem?.code === stepUpRequiredCode;
}

/** The owner closed the password dialog: the action was not performed. Not an error to show. */
export class StepUpCancelledError extends Error {
  constructor() {
    super('Password confirmation was cancelled.');
    this.name = 'StepUpCancelledError';
  }
}

export function isStepUpCancelled(error: unknown): boolean {
  return error instanceof StepUpCancelledError;
}
