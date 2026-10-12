import type { AddMembersResult, RemoveMembersResult, TransferResult } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

export const plural = (count: number, one: string, many: string): string =>
  `${formatNumber(count)} ${count === 1 ? one : many}`;

/** "NSE:TCS" for one instrument, else "3 instruments". */
export const subject = (count: number, label: string): string =>
  count === 1 ? label : plural(count, 'instrument', 'instruments');

const trackedNote = (count: number): string =>
  count > 0
    ? `${formatNumber(count)} ${count === 1 ? 'was' : 'were'} not tracked and now ${count === 1 ? 'is' : 'are'}`
    : '';

const join = (head: string, notes: readonly string[]): string => {
  const parts = notes.filter((note) => note !== '');
  return parts.length > 0 ? `${head} (${parts.join('; ')})` : head;
};

/** "Copied NSE:TCS to Best stocks (1 was already there)" / "Moved 3 instruments to MyChoice". */
export function transferMessage(result: TransferResult, mode: 'copy' | 'move', targetName: string, label: string) {
  // A copy counts what is in the target now; a move what left the source (some may have been in the target already).
  const done = mode === 'copy' ? result.added + result.alreadyInTarget : result.removed;
  const head = `${mode === 'copy' ? 'Copied' : 'Moved'} ${subject(done, label)} to ${targetName}`;
  return join(head, [
    result.alreadyInTarget > 0 ? `${formatNumber(result.alreadyInTarget)} already there` : '',
    result.notInSource > 0 ? `${formatNumber(result.notInSource)} no longer in this basket` : '',
    trackedNote(result.newlyTracked),
  ]);
}

/** "Added NSE:TCS to MyChoice (…)": one basket or several. */
export function addedMessage(
  result: AddMembersResult,
  basketNames: readonly string[],
  label: string,
  newlyTracked = result.newlyTracked,
): string {
  const head = `Added ${subject(result.requested, label)} to ${basketNames.join(' and ')}`;
  return join(head, [
    result.alreadyMembers > 0 ? `${formatNumber(result.alreadyMembers)} already there` : '',
    trackedNote(newlyTracked),
  ]);
}

export function removedMessage(result: RemoveMembersResult, basketName: string, label: string): string {
  const head = `Removed ${subject(result.removed, label)} from ${basketName}`;
  return result.notMembers > 0 ? join(head, [`${formatNumber(result.notMembers)} were not in it`]) : head;
}
