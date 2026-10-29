import type { ReportMetric } from '../api/reportQueries';
import { stateWords } from './executionLabels';

export const verdictWords: Readonly<Record<string, string>> = {
  within: 'Within the margin',
  not_within: 'Not within the margin',
  inconclusive: 'Inconclusive',
  insufficient: 'Insufficient pairs',
};

export const reasonWords: Readonly<Record<string, string>> = {
  live_only: 'Live only (no paper entry)',
  paper_only: 'Paper only (not taken live)',
  live_open: 'Live position not closed',
  live_uncertain: 'Live trip in doubt (no R)',
  paper_open: 'Paper trade not closed',
  no_plan_levels: 'No stop, no risk per share',
  plan_not_graded: 'Plan not graded',
  unknown: 'Outcome unknown',
};

export const readOnlyNote =
  'Read only: this tab compares your live fills with the paper account on the same tickets. It changes no setting and places no order.';

/** R as text to `digits` places, only for a value; any other state is its word (never a digit). */
export function r(metric: ReportMetric, digits = 3): string {
  return metric.state === 'value' && metric.number !== null
    ? metric.number.toFixed(digits)
    : (stateWords[metric.state] ?? metric.state).toLowerCase();
}

/** The interval as text, or the state word when an end is not a value. */
export function intervalText(lo: ReportMetric, hi: ReportMetric): string {
  return lo.state === 'value' && hi.state === 'value' && lo.number !== null && hi.number !== null
    ? `${lo.number.toFixed(3)} to ${hi.number.toFixed(3)} R`
    : r(lo);
}
