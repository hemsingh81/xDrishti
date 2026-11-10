import { ApiError } from '@/shared/api';

/** The words of a refused Auto act: the server's sentence and code as sent, and that nothing changed. */
export function autoProblem(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was changed.';
  const p = error.problem;
  if (error.status === 401 && p?.code === 'auth.invalid_credentials')
    return 'The password was not accepted. Nothing was changed.';
  if (error.status === 423) return `Too many wrong passwords; sign-in is locked for now. ${p?.detail ?? ''}`.trim();
  const code = p?.code ? ` (${p.code})` : '';
  const text = [p?.title, p?.detail].filter(Boolean).join(' ') || 'The request failed.';
  return `${text}${code} Nothing was changed.`;
}

/** Preconditions that are not a pass: `unavailable` is never shown as a pass. */
export const statusLabel = (status: string) =>
  status === 'pass' ? 'Pass' : status === 'fail' ? 'Fail' : 'Unavailable (not a pass)';
