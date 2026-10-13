import { ApiError, type ServiceIssue } from '@/shared/api';

/** Plain-words text for a failed request (the API's problem title when it sent one). */
export function problemText(error: unknown, fallback: string): string {
  if (error instanceof ApiError) return error.problem?.title ?? fallback;
  return 'Could not reach the server. Check your connection and try again.';
}

export const codeOf = (error: unknown): string | undefined =>
  error instanceof ApiError ? error.problem?.code : undefined;

/** The checks a refused save (`services.invalid`) names, as the dialog shows them. */
export function issuesOf(error: unknown, serviceId: string): ServiceIssue[] {
  if (!(error instanceof ApiError) || error.problem?.code !== 'services.invalid') return [];
  return (error.problem.issues ?? []).map((issue) => ({
    serviceId: issue.serviceId ?? serviceId,
    field: issue.field,
    severity: issue.severity,
    code: issue.code,
    message: issue.message,
    relatedServiceId: null,
  }));
}

/** The sentence for a save, restore or preview that did not work and has no per-field place. */
export function saveErrorText(error: unknown): string | null {
  switch (codeOf(error)) {
    case 'services.invalid':
    case 'services.revision_changed':
      return null;
    case 'services.read_only':
      return 'This entry is set elsewhere and cannot be changed here.';
    case 'services.not_found':
      return 'This service no longer exists.';
    default:
      return problemText(error, 'The schedule could not be saved.');
  }
}

/** The sentence for a Run now, Pause or Resume that did not work. */
export function actionErrorText(error: unknown, name: string, fallback: string): string {
  switch (codeOf(error)) {
    case 'services.already_running':
      return `${name} is already running. This page shows its progress.`;
    case 'services.read_only':
      return `${name} is set elsewhere and cannot be changed here.`;
    case 'services.not_found':
      return `${name} no longer exists.`;
    default:
      return problemText(error, fallback);
  }
}
