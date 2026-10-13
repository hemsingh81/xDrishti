import { ApiError, type PortfolioTrackResult } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { problemText } from './errors';

const plural = (n: number, one: string, many: string) => `${formatNumber(n)} ${n === 1 ? one : many}`;

/** The API's hint is a sentence ("Run Update data …."): it follows the dash in lower case, without the full stop. */
function hintAfterDash(hint: string): string {
  const text = hint.trim().replace(/\.$/, '');
  const [first = '', second = ''] = text;
  return first !== '' && first === first.toUpperCase() && second === second.toLowerCase()
    ? first.toLowerCase() + text.slice(1)
    : text;
}

const withHint = (lead: string, hint: string): string =>
  hint.trim() === '' ? lead : `${lead} — ${hintAfterDash(hint)}`;

/** The toast of "Track my holdings": what was added, what could not be, and what to do next (the API's hint). */
export function trackHoldingsToast(result: PortfolioTrackResult): { message: string; severity: 'success' | 'info' } {
  const notes = [
    result.notInMaster > 0
      ? `${plural(result.notInMaster, 'holding is', 'holdings are')} not in the instrument master`
      : '',
    result.retired > 0 ? `${plural(result.retired, 'instrument is', 'instruments are')} retired and skipped` : '',
  ].filter(Boolean);
  let lead: string;
  if (result.added > 0) lead = `Tracked ${plural(result.added, 'more holding', 'more holdings')}`;
  else if (result.alreadyTracked > 0) lead = 'Your holdings are already tracked';
  else lead = 'No open holdings could be tracked';
  const message = notes.length > 0 ? `${lead}; ${notes.join('; ')}` : lead;
  return {
    message: result.added > 0 || result.alreadyTracked > 0 ? withHint(message, result.hint) : message,
    severity: result.added > 0 ? 'success' : 'info',
  };
}

/** The toast of "Track the benchmark". */
export function trackBenchmarkToast(
  result: PortfolioTrackResult,
  name: string,
): { message: string; severity: 'success' | 'info' } {
  const lead = result.added > 0 ? `Tracking ${name}` : `${name} is already tracked`;
  return { message: withHint(lead, result.hint), severity: result.added > 0 ? 'success' : 'info' };
}

/** Why a track action failed: the API's own title (its 409 is not about lots here), the network text when unreachable. */
export function trackErrorText(error: unknown, fallback: string): string {
  if (error instanceof ApiError) return error.problem?.title ?? fallback;
  return problemText(error, fallback);
}
