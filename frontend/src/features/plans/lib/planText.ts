import { ApiError } from '@/shared/api';

/** The pinned sentence of the server (PlanWording.ApprovedForWatching): shown next to every decision. */
export const noOrderWording = 'Approved for watching: no order is placed and none exists.';
export const lapseWording = 'Undecided tickets lapse at the cutoff.';

const stateLabels: Record<string, string> = {
  draft: 'Draft',
  published: 'Published',
  inreview: 'In review',
  approved: 'Approved',
  rejected: 'Rejected',
  lapsed: 'Lapsed',
  armed: 'Armed',
  expired: 'Expired',
  proposed: 'Proposed',
  modified: 'Modified',
};

/** Words for a plan or ticket state; unknown states are shown as sent. */
export const stateLabel = (state: string): string => stateLabels[state.toLowerCase()] ?? state;

export interface BasisFlags {
  provisional: boolean;
  gateUnavailable: boolean;
  openBookUnavailable: boolean;
}

/** The exact markers the backend writes into the basis (PlanWording.GateUnavailableMarker, OpenBookUnavailableMarker). */
export const gateUnavailableMarker = 'market gate unavailable';
export const openBookUnavailableMarker = 'Open book unavailable';

/** The basis flags: the backend's exact marker strings, matched as it matches them (ordinal, case-sensitive Contains). */
export function basisFlags(plan: { basis: string; provisional: boolean }): BasisFlags {
  return {
    provisional: plan.provisional,
    gateUnavailable: plan.basis.includes(gateUnavailableMarker),
    openBookUnavailable: plan.basis.includes(openBookUnavailableMarker),
  };
}

/** "1h 05m 09s left" or "Cutoff passed". Pure: tested without a clock. */
export function formatCountdown(cutoffMs: number, nowMs: number): string {
  const left = Math.floor((cutoffMs - nowMs) / 1000);
  if (left <= 0) return 'Cutoff passed';
  const h = Math.floor(left / 3600);
  const m = Math.floor((left % 3600) / 60);
  const s = left % 60;
  const pad = (n: number) => String(n).padStart(2, '0');
  return h > 0 ? `${h}h ${pad(m)}m ${pad(s)}s left` : `${m}m ${pad(s)}s left`;
}

/** What a failed decision means for the owner, in words (never the raw server text alone). */
export function decisionError(error: unknown): string {
  if (!(error instanceof ApiError)) return error instanceof Error ? error.message : 'Something went wrong.';
  const code = error.problem?.code;
  if (error.status === 401) return 'Your session has ended. Sign in again; nothing was changed.';
  if (error.status === 403)
    return 'The request was refused (security token or owner check). Reload the page; nothing was changed.';
  if (code === 'plans.closed')
    return 'The review cutoff has passed: approving and changing are closed. Undecided tickets lapse.';
  if (code === 'plans.stale' || code === 'plans.move_refused')
    return 'This ticket was already decided or changed. The screen now shows its current state.';
  if (code === 'plans.checklist_blocked')
    return `A check blocks this approval. ${error.problem?.detail ?? error.message}`;
  return error.problem?.detail ?? error.message;
}
