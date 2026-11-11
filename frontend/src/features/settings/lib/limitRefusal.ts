import { ApiError } from '@/shared/api';
import { fieldMessages } from './problems';

/** The words of a refused limit change. The server's field messages are shown as sent; a wrong password is an answer, not a sign-out. */
export function limitRefusal(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was changed.';
  const title = error.problem?.title ?? 'The request failed.';
  switch (error.status) {
    case 401:
      return 'The password was not accepted. Nothing was changed.';
    case 403:
      return `${title} Nothing was changed.`;
    case 423:
      return `Too many wrong passwords; sign-in is locked for now. ${title}`;
    case 409:
      return 'These settings were changed elsewhere. They were read again: check the values against what is in force and try again. Nothing was changed.';
    case 400:
    case 422: {
      const fields = Object.values(fieldMessages(error));
      return `${fields.length > 0 ? fields.join(' ') : title} Nothing was changed.`;
    }
    default:
      return `${title} Nothing was changed.`;
  }
}
