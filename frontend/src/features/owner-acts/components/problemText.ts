import { ApiError } from '@/shared/api';
import { formatDuration } from '@/shared/lib/format';

export interface StartProblem {
  text: string;
  /** The job a 409 names, so the screen can link to it. */
  activeJobId?: string;
}

const uuid = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i;

/** The words of a refused start. Server text is shown as sent (as text); nothing was started in any of these cases. */
export function startProblem(error: unknown, fallbackJobId?: string | null): StartProblem {
  if (!(error instanceof ApiError)) return { text: 'Could not reach the server. Nothing was started.' };
  const p = error.problem;
  const server = [p?.title, p?.detail].filter(Boolean).join(' ');
  switch (error.status) {
    case 401:
      return { text: 'The password was not accepted. Nothing was started.' };
    case 403:
      return { text: `This act is switched off. ${server}`.trim() };
    case 409: {
      const id = uuid.exec(server)?.[0] ?? fallbackJobId ?? undefined;
      return { text: `A replay is already queued or running. ${server}`.trim(), ...(id ? { activeJobId: id } : {}) };
    }
    case 423:
      return { text: `Too many wrong passwords; sign-in is locked for now. ${server}`.trim() };
    case 429: {
      const wait = p?.retryAfterSeconds;
      return { text: `${server || 'Limit reached.'}${wait ? ` Try again in ${formatDuration(wait)}.` : ''}`.trim() };
    }
    default:
      return { text: server || 'The request failed. Nothing was started.' };
  }
}
