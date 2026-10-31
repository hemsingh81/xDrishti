import { metricOf, type MetricState } from '@/shared/analytics';
import type { Schemas } from '@/shared/api';

/** The server's `{state, value?, reason?, n?}` as the kit's honest states: a non-value never becomes a number. */
export function execMetric(m: Schemas['ExecMetric']): MetricState {
  if (m.state === 'value') return metricOf(m.value, 'The server sent no figure.');
  const reason = m.reason ?? undefined;
  if (m.state === 'empty') return reason ? { kind: 'empty', reason } : { kind: 'empty' };
  if (m.state === 'not_evaluable') return { kind: 'not_evaluable', reason: reason ?? 'Not evaluable.' };
  if (m.state === 'insufficient') return { kind: 'unavailable', reason: reason ?? `Not enough data (n ${m.n ?? 0}).` };
  return { kind: 'unavailable', reason: reason ?? 'Not available.' };
}
