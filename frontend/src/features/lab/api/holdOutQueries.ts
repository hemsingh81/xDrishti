import { queryOptions } from '@tanstack/react-query';
import { api, ApiError, unwrap, type Schemas } from '@/shared/api';

export type HoldOutOverview = Schemas['HoldOutView'];
export type HoldOutBlock = Schemas['HoldOutBlockView'];
export type HoldOutLook = Schemas['HoldOutLookView'];

export const holdOutKey = ['lab', 'holdout'] as const;

/** The chain of hold-out blocks with their states and looks (HO-01). */
export const holdOutQuery = () =>
  queryOptions({
    queryKey: holdOutKey,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/lab/holdout', { signal })),
  });

/** Owner act: the password travels in the body of this one call and is kept nowhere. */
export async function releaseHoldOutBlock(start: string, password: string, forfeitLook: boolean) {
  return unwrap(
    await api.POST('/api/lab/holdout/blocks/{start}/release', {
      params: { path: { start } },
      body: { password, forfeitLook },
    }),
  );
}

/** What a refused release says in plain words; the server's sentence follows as text. */
export function releaseProblem(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. The block was not released.';
  switch (error.problem?.code) {
    case 'lab.holdout_filling':
      return 'This block is still filling: its last session has not closed.';
    case 'lab.holdout_order':
      return 'Only the earliest unreleased block can be released.';
    case 'lab.holdout_unlooked':
      return 'This block has no fresh look. Releasing it forfeits its looks for good; tick the box to confirm.';
    case 'lab.holdout_released':
      return 'This block is released already.';
    default:
      break;
  }
  if (error.status === 401) return 'The password was not accepted. The block was not released.';
  if (error.status === 423) return 'Too many wrong passwords; sign-in is locked for now.';
  return `${[error.problem?.title, error.problem?.detail].filter(Boolean).join(' ') || 'The request failed.'} The block was not released.`;
}

/** Words for what keeps the automatic release from acting (the `blockedBy` of the block in force). */
export const blockedByWords: Record<string, string> = {
  filling: 'The block is still filling.',
  unlooked: 'No fresh look was taken yet; the automatic release never forfeits a look.',
  look_available: 'A look is still available (a strategy version, a model or a system run can take it).',
  unavailable: 'A read failed, so the automatic release keeps the block.',
  no_strategy_slot: 'The strategy look is not registered, so the automatic release cannot decide.',
  auto_release_off: 'The automatic release is off; release the block yourself.',
};

export const lookWords: Record<string, string> = {
  fresh: 'Taken',
  available: 'Still available',
  no_subject: 'No subject',
  unavailable: 'Unreadable',
  none: 'Not read',
};
