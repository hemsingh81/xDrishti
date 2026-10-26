import type { MetricState } from '@/shared/analytics';
import type { MetricView } from '../api/paperQueries';

/** A wire figure as the kit's state: only `value` carries a digit; every other state is its word and reason. */
export function toMetric(view: MetricView): MetricState {
  if ((view.state === 'value' || view.state === 'provisional') && view.number !== null) {
    return view.state === 'value'
      ? { kind: 'value', value: view.number }
      : { kind: 'provisional', value: view.number, reason: view.reason ?? 'provisional' };
  }
  const reason = view.reason ?? undefined;
  if (view.state === 'empty') return { kind: 'empty', ...(reason ? { reason } : {}) };
  if (view.state === 'not_evaluable') return { kind: 'not_evaluable', reason: reason ?? 'not evaluable' };
  const word = view.state === 'insufficient' ? 'Insufficient data: ' : '';
  return { kind: 'unavailable', reason: `${word}${reason ?? 'no reason given'}` };
}
