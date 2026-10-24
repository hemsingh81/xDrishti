import type { ReviewAccount, ReviewTicket } from '../api/planQueries';
import { stateLabel } from './planText';

/** The check codes of one account that need the owner's confirmation. */
export const confirmableCodes = (account: ReviewAccount): string[] =>
  account.checks.filter((c) => c.standing.toLowerCase() === 'needsconfirmation').map((c) => c.code);

export const decidable = (state: string) => ['proposed', 'modified'].includes(state.toLowerCase());

/** Why Approve is disabled, or null when it can be pressed. */
export function approveBlock(t: ReviewTicket, reviewOpen: boolean): string | null {
  if (!decidable(t.state)) return `Already ${stateLabel(t.state).toLowerCase()}.`;
  if (!reviewOpen) return 'Decisions are closed: the cutoff has passed or the plan is closed.';
  if (!t.checklistRecorded)
    return 'No readable checklist was stored for this ticket, so it cannot be approved (a missing record is not a pass).';
  if (!t.canApprove) {
    const codes = [...new Set(t.accounts.flatMap(confirmableCodes))];
    return codes.length > 0
      ? `Checks need your confirmation: ${codes.join(', ')}.`
      : 'A check blocks approval and cannot be confirmed.';
  }
  return null;
}
