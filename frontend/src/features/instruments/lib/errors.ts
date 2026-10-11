import { ApiError } from '@/shared/api';

/** Plain-words text for a failed request (the API's problem title when it sent one). */
export function problemText(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    return error.problem?.title ?? fallback;
  }
  return 'Could not reach the server. Check your connection and try again.';
}

/** 409 `instruments.refresh_running`: somebody (or the daily run) is already refreshing — just watch it. */
export const isRefreshRunning = (error: unknown): boolean => error instanceof ApiError && error.status === 409;
