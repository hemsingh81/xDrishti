import type { ResolvedInstrument, TrackResult, UntrackResult } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

const plural = (count: number, one: string, many: string) => `${formatNumber(count)} ${count === 1 ? one : many}`;

/** "NSE:TCS tracked" for one instrument, else "Tracked 12 instruments (10 new, 2 tracked again, 3 already tracked)". */
export function trackedMessage(result: TrackResult, label: string): string {
  if (result.requested === 1) {
    return result.alreadyTracked === 1 ? `${label} is already tracked` : `${label} tracked`;
  }
  const parts = [
    result.added > 0 ? `${formatNumber(result.added)} new` : null,
    result.reactivated > 0 ? `${formatNumber(result.reactivated)} tracked again` : null,
    result.alreadyTracked > 0 ? `${formatNumber(result.alreadyTracked)} already tracked` : null,
  ].filter((part): part is string => part !== null);
  const done = result.added + result.reactivated;
  return `Tracked ${plural(done, 'instrument', 'instruments')}${parts.length > 0 ? ` (${parts.join(', ')})` : ''}`;
}

/** "NSE:TCS untracked — its history is kept", else "Untracked 5 instruments (2 were not tracked) — their history is kept". */
export function untrackedMessage(result: UntrackResult, label: string): string {
  if (result.requested === 1) {
    return result.notTracked === 1 ? `${label} was not tracked` : `${label} untracked — its history is kept`;
  }
  const note = result.notTracked > 0 ? ` (${formatNumber(result.notTracked)} were not tracked)` : '';
  return `Untracked ${plural(result.untracked, 'instrument', 'instruments')}${note} — their history is kept`;
}

const unmatchedReasons: Readonly<Record<string, string>> = {
  not_found: 'Not found in the master list',
  ambiguous: 'Several instruments share this ISIN',
  invalid: 'Not a valid symbol',
};

export const unmatchedReason = (reason: string): string => unmatchedReasons[reason] ?? reason;

/** Matched instruments that Track would add now: not tracked yet and still in Dhan's list. */
export const newlyTrackable = (matched: readonly ResolvedInstrument[]): ResolvedInstrument[] =>
  matched.filter((m) => !m.tracked && !m.retired);
