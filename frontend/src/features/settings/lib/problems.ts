import { ApiError } from '@/shared/api';

/** The per-field messages of a 400 (keys as the API names them, e.g. `belowCostHighPct`); several are joined. */
export function fieldMessages(error: unknown): Record<string, string> {
  const errors = error instanceof ApiError ? error.problem?.errors : undefined;
  return errors ? Object.fromEntries(Object.entries(errors).map(([field, list]) => [field, list.join(' ')])) : {};
}

/** Plain words for a failed request that is not about a single field. */
export function problemText(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Check your connection and try again.';
  return error.problem?.title ?? fallback;
}
