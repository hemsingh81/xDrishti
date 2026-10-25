import type { EvidenceProps } from '@/shared/analytics';
import type { Schemas } from '@/shared/api';

type Evidence = Schemas['ReportEvidenceView'];
type GroupEvidence = Schemas['GroupEvidenceView'];

/** The kit's evidence from the server's: n, range, basis ("Gross R" unless the server says net) and provisional. */
export function evidenceOf(
  evidence: Evidence,
  over: { n?: number; label?: string } = {},
): Omit<EvidenceProps, 'compact'> {
  return {
    n: over.n ?? evidence.n,
    range: evidence.firstDate && evidence.lastDate ? { from: evidence.firstDate, to: evidence.lastDate } : null,
    basis: evidence.basis === 'net' ? 'net' : 'gross',
    provisional: evidence.provisional,
    population: evidence.runs.map((run) => `${run.strategyId} ${run.universe}`),
    limits: evidence.limits,
    label: over.label,
  };
}

/** The evidence beside one group: its own n and dates, the report's basis. */
export function groupEvidenceOf(group: GroupEvidence, report: Evidence): Omit<EvidenceProps, 'compact'> {
  return {
    ...evidenceOf(report),
    n: group.n,
    range: group.firstDate && group.lastDate ? { from: group.firstDate, to: group.lastDate } : null,
    provisional: group.provisional,
  };
}

/** The line printed under every basket-based figure. */
export const NON_ADDITIVE_NOTE = 'A trade in several baskets appears under each; totals are not additive.';
