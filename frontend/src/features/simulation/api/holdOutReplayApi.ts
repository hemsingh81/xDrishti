import { queryOptions } from '@tanstack/react-query';
import { api, ApiError, unwrap, type Schemas } from '@/shared/api';

export type HoldOutRegistration = Schemas['HoldOutRegistrationView'];
export type HoldOutRegistrationResult = Schemas['HoldOutRegistrationResultView'];
export type HoldOutAutoSettings = Schemas['HoldOutAutoSettingsView'];

export const holdOutReplayKey = ['simulation', 'holdout-replay'] as const;

/** The pre-registrations of the system replay over a hold-out block, newest first (SR-03). */
export const holdOutRegistrationsQuery = queryOptions({
  queryKey: [...holdOutReplayKey, 'registrations'],
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/replay/holdout-registrations', { signal })),
});

/** The two automatic switches (default on). A null is unavailable. */
export const holdOutSettingsQuery = queryOptions({
  queryKey: [...holdOutReplayKey, 'settings'],
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/replay/settings', { signal })),
});

/** Owner session: stores the registration for the next block that has not begun. */
export async function registerHoldOutReplay(costSheetId: string) {
  return unwrap(await api.POST('/api/replay/holdout-registrations', { body: { costSheetId } }));
}

/** Owner act: the password travels in the body of this one call and is kept nowhere. Irreversible: the look is burned. */
export async function runHoldOutReplay(id: string, password: string) {
  return unwrap(
    await api.POST('/api/replay/holdout-registrations/{id}/run', { params: { path: { id } }, body: { password } }),
  );
}

/** Owner act: sets one automatic switch. */
export async function setHoldOutSwitch(name: 'autoRegister' | 'autoRun', enabled: boolean, password: string) {
  return unwrap(await api.PUT('/api/replay/settings', { body: { password, [name]: enabled } }));
}

/** What a refused call says in plain words; the server's sentence follows for any other refusal. */
export function holdOutReplayProblem(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was changed.';
  switch (error.problem?.code) {
    case 'replay.registration_drift':
      return 'A stored input is missing or the charge sheet changed. Nothing was claimed and no price of the block was read.';
    case 'replay.holdout_used':
      return 'The system look of this block is used already; the stored result is unchanged.';
    case 'lab.holdout_filling':
      return 'The block is still filling: its last session has not closed.';
    case 'replay.registration_superseded':
      return 'A newer registration targets this block; only the newest one runs.';
    default:
      break;
  }
  if (error.status === 401) return 'The password was not accepted. Nothing was changed.';
  if (error.status === 403) return 'Enter your password for this act.';
  if (error.status === 423) return 'Too many wrong passwords; sign-in is locked for now.';
  return [error.problem?.title, error.problem?.detail].filter(Boolean).join(' ') || 'The request failed.';
}

/** The words of a registration's state. */
export const stateWords: Record<string, string> = {
  waiting: 'Waiting for the block to seal',
  superseded: 'Superseded by a newer registration',
  drift: 'Drift: a stored input changed',
  claimed: 'Look claimed, the run follows',
  run: 'Run',
};
