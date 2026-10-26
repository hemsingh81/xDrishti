import { ApiError } from '@/shared/api';

/** The state words of the API in the words of the screen. */
export const stateWords: Record<string, string> = {
  not_confirmed: 'Not confirmed',
  confirmed: 'Confirmed',
  stale: 'Stale',
  sheet_missing: 'Sheet missing',
  revoked: 'Revoked',
};

export const kindWords: Record<string, string> = {
  confirm: 'Confirmed',
  revoke: 'Revoked',
  gate_on: 'Confirming allowed',
  gate_off: 'Confirming stopped',
};

/** The plain words for a refused or failed act; a wrong password is said as such and never signs the owner out. */
export function actProblemText(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was changed.';
  if (error.status === 401) return 'The password was not accepted. Nothing was changed.';
  return error.problem?.title ?? 'The request failed. Nothing was changed.';
}
