import type { MetricView } from '../api/executionQueries';

/** The words for the states of a figure; a state that is not a value never shows a digit. */
export const stateWords: Readonly<Record<string, string>> = {
  value: 'Estimate',
  provisional: 'Provisional',
  insufficient: 'Insufficient',
  unavailable: 'Unavailable',
  not_evaluable: 'Not evaluable',
  empty: 'Empty',
};

export const referenceWords: Readonly<Record<string, string>> = {
  ticket_level: 'ticket level',
  plan_grade: 'plan grade',
  unavailable: 'no reference',
};

export const emptySentence = 'No estimates yet: run `xd live estimates` or switch on the weekly service.';

export const slippageSentence =
  'Slippage is in basis points of the reference price; positive is worse for you. For a resting limit order the execution part includes the wait for the market to arrive. Nothing here is applied to the simulator or the selection.';

/** A figure as text: the number to `digits` places only for a value, otherwise the state word. */
export function figure(metric: MetricView, digits: number): string {
  return (metric.state === 'value' || metric.state === 'provisional') && metric.number !== null
    ? metric.number.toFixed(digits)
    : (stateWords[metric.state === 'value' ? 'unavailable' : metric.state] ?? metric.state).toLowerCase();
}

/** An interval as text ("1.13 to 4.17"), or an empty string when either end is not a value. */
export function interval(lo: MetricView, hi: MetricView, digits: number): string {
  return lo.number !== null && hi.number !== null && lo.state === 'value' && hi.state === 'value'
    ? `${lo.number.toFixed(digits)} to ${hi.number.toFixed(digits)}`
    : '';
}
