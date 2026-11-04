import { queryOptions } from '@tanstack/react-query';
import { api, ApiError, unwrap, type Schemas } from '@/shared/api';

export type Registration = Schemas['RegistrationView'];
export type RegistrationInput = Schemas['RegistrationRequest'];
/** The five texts as typed: every part is a string (the request type also allows null). */
export type RegistrationText = Record<keyof RegistrationInput, string>;
export type ReportHoldOut = Schemas['ReportHoldOutView'];
export type ReportRegistration = Schemas['ReportRegistrationView'];
export type Trials = Schemas['TrialsView'];
export type HoldOutResult = Schemas['HoldOutResultView'];

/** Longest text of one part of the registration (the API refuses more). */
export const maxPartLength = 2000;

export const registrationKey = (strategyId: string, version: number) =>
  ['lab', 'registration', strategyId, version] as const;

/** The registration of one version, or `null` when there is none (a 404 is an ordinary answer here, not a failure). */
export const registrationQuery = (strategyId: string, version: number) =>
  queryOptions({
    queryKey: registrationKey(strategyId, version),
    queryFn: async ({ signal }): Promise<Registration | null> => {
      const result = await api.GET('/api/lab/strategies/{id}/versions/{version}/registration', {
        params: { path: { id: strategyId, version } },
        signal,
      });
      if (result.response.status === 404 && result.error?.code === 'lab.not_found') return null;
      return unwrap(result);
    },
  });

export async function registerVersion(strategyId: string, version: number, input: RegistrationText) {
  return unwrap(
    await api.PUT('/api/lab/strategies/{id}/versions/{version}/registration', {
      params: { path: { id: strategyId, version } },
      body: input,
    }),
  );
}

/** Owner act: the password travels in the body of this one call and is kept nowhere. */
export async function unlockHoldOut(runId: string, password: string) {
  return unwrap(
    await api.POST('/api/lab/runs/{id}/holdout-unlock', { params: { path: { id: runId } }, body: { password } }),
  );
}

/** What a refused unlock or registration says in plain words; the server's sentence follows as text. */
export function confidenceProblem(error: unknown, nothing: string): string {
  if (!(error instanceof ApiError)) return `Could not reach the server. ${nothing}`;
  const p = error.problem;
  const server = [p?.title, p?.detail].filter(Boolean).join(' ');
  const say = (...parts: string[]) => parts.filter(Boolean).join(' ');
  switch (p?.code) {
    case 'lab.registration_frozen':
      return say(
        'This version can no longer be registered: it has a registration already, or a full test has fixed its plan. Register a new version instead.',
        nothing,
      );
    case 'lab.registration_exists':
      return say('This version is already registered; the text is frozen.', nothing);
    case 'lab.holdout_used':
      return 'This version has already looked at the hold-out. The stored result is unchanged and is shown in the report.';
    case 'lab.holdout_unavailable':
      return say('The hold-out cannot be opened for this run.', server, nothing);
    case 'lab.no_report':
      return say('This run has no report to open the hold-out from.', nothing);
    default:
      break;
  }
  if (error.status === 401) return say('The password was not accepted.', nothing);
  if (error.status === 423) return say('Too many wrong passwords; sign-in is locked for now.', nothing);
  return say(server || 'The request failed.', nothing);
}
