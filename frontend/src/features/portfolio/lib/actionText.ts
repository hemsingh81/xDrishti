import type { ActionItem, ActionCounts } from '@/shared/api';
import { formatDate, formatNumber } from '@/shared/lib/format';
import { problemKind, problemText } from './errors';
import { subjectOf } from './actions';
import type { Decision } from './actionDecision';

/** The toast after a decision: "Snoozed RELIANCE until 20 Oct 2026." */
export function decidedText(decision: Decision, item: ActionItem, snoozedUntil: string | null): string {
  const subject = subjectOf(item);
  switch (decision) {
    case 'snooze':
      return snoozedUntil ? `Snoozed ${subject} until ${formatDate(snoozedUntil)}.` : `Snoozed ${subject} for 7 days.`;
    case 'done':
      return `Marked ${subject} done. It comes back if the reason goes away and then returns.`;
    case 'reopen':
      return `Reopened ${subject}.`;
  }
}

const decisionVerbs: Record<Decision, string> = { snooze: 'snooze', done: 'mark', reopen: 'reopen' };

/** Why a decision was refused, in plain words (the list has been put back and read again). */
export function decisionProblemText(error: unknown, decision: Decision, item: ActionItem): string {
  const subject = subjectOf(item);
  switch (problemKind(error)) {
    case 'not_found':
      return `${subject} is no longer on the list: what it was about has changed. The list was reloaded.`;
    case 'conflict':
      return `${subject} was changed meanwhile. The list was reloaded: look at it again.`;
    default:
      return problemText(
        error,
        `Could not ${decisionVerbs[decision]} ${subject}${decision === 'done' ? ' done' : ''}. Nothing was changed.`,
      );
  }
}

/** The note under the filter chips about rules or holdings the figures could not cover; null when there is nothing to say. */
export function coverageNotes(counts: ActionCounts): string[] {
  const notes: string[] = [];
  const lagging = counts.unavailable.lagging;
  if (lagging > 0) {
    notes.push(
      `1 rule not evaluated: Lagging Nifty needs Nifty 50 history (${formatNumber(lagging)} ${
        lagging === 1 ? 'holding' : 'holdings'
      } affected).`,
    );
  }
  if (counts.noPrice > 0) {
    notes.push(
      `${formatNumber(counts.noPrice)} ${counts.noPrice === 1 ? 'holding' : 'holdings'} without a price ${
        counts.noPrice === 1 ? 'is' : 'are'
      } not checked by the price rules.`,
    );
  }
  return notes;
}

/** The words under the list when a status has no items. */
export const emptyListText = {
  open: 'Nothing needs a decision.',
  snoozed: 'Nothing is snoozed.',
  done: 'Nothing is marked done.',
} as const;
