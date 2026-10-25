import type { MetricState } from '@/shared/analytics';
import type { Schemas } from '@/shared/api';

type Metric = Schemas['MetricView'];

/** A panel that has nothing to draw prints a state, never a number: a stray value there reads as "no records". */
export function asNonValue(state: MetricState): Exclude<MetricState, { kind: 'value' }> {
  return state.kind === 'value' ? { kind: 'empty' } : state;
}

/**
 * The kit state of a server figure. `n` is the group's trades and `min` the minimum sample (`what-works.minTrades`) when one is
 * at hand; without it an `insufficient` figure is `not_evaluable` and prints the server's reason (no digit) instead of "of at least N". A `value` without a
 * number is `unavailable`, never 0; a state this client does not know is `unavailable` too.
 */
export function metricFromApi(view: Metric, n?: number | null, min?: number | null): MetricState {
  const reason = view.reason ?? undefined;
  switch (view.state) {
    case 'value':
      return typeof view.number === 'number'
        ? { kind: 'value', value: view.number }
        : { kind: 'unavailable', reason: 'The server sent no number.' };
    case 'provisional':
      return typeof view.number === 'number'
        ? { kind: 'provisional', value: view.number, reason: reason ?? 'Provisional' }
        : { kind: 'unavailable', reason: 'The server sent no number.' };
    case 'insufficient':
      return typeof n === 'number' && typeof min === 'number'
        ? { kind: 'insufficient', n, min }
        : { kind: 'not_evaluable', reason: `Insufficient data: ${reason ?? 'too few trades for a figure.'}` };
    case 'not_evaluable':
      return { kind: 'not_evaluable', reason: reason ?? 'The figure cannot be evaluated.' };
    case 'empty':
      return { kind: 'empty', reason };
    case 'unavailable':
      return { kind: 'unavailable', reason: reason ?? 'The input does not exist.' };
    default:
      return { kind: 'unavailable', reason: reason ?? 'Unknown state.' };
  }
}
