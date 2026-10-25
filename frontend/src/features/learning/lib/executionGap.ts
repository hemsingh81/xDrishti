import type { MetricState } from '@/shared/analytics';
import type { PlanVsActual } from '../api/learningQueries';
import { metricStateOf } from './metricView';

/**
 * The execution gap: the mean realised R minus the mean plan R over the tickets graded on fills. It is a value only when both
 * means are; with no graded ticket it is not evaluable (never a zero), and below the minimum it is insufficient.
 */
export function executionGapState(view: PlanVsActual | undefined, failed: boolean): MetricState {
  if (failed) return { kind: 'unavailable', reason: 'Plan versus actual could not be read.' };
  if (!view) return { kind: 'loading' };
  const plan = view.chain.find((s) => s.step === 'plan_prices');
  const actual = view.chain.find((s) => s.step === 'actual');
  if (!plan || !actual || view.counts.gradedOnFills === 0) {
    return { kind: 'not_evaluable', reason: 'No ticket has been graded on fills yet' };
  }
  const p = metricStateOf(plan.metric);
  const a = metricStateOf(actual.metric);
  if (p.kind === 'value' && a.kind === 'value') return { kind: 'value', value: a.value - p.value };
  return p.kind === 'value' ? a : p;
}
