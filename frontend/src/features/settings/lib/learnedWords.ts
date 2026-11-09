import { ApiError } from '@/shared/api';

/** The server's words for a refused call (its detail, else its title). */
export function refusalText(error: unknown): string {
  if (error instanceof ApiError) return error.problem?.detail ?? error.problem?.title ?? error.message;
  return 'Could not reach the server. Nothing was changed.';
}

const stateText: Record<string, string> = {
  proposed: 'Waiting for you',
  applied: 'Applied',
  dismissed: 'Dismissed',
  undone: 'Undone',
};

export const stateLabel = (state: string): string => stateText[state] ?? state;

/** `auto` is shown as the system's own decision, never as an owner approval. */
export const decidedByText = (decidedBy: string | null): string =>
  decidedBy === null
    ? ''
    : decidedBy === 'auto'
      ? 'Decided automatically (recorded as auto)'
      : `Decided by ${decidedBy.replace(/^owner:/, '')}`;
