import type { MetricState } from '@/shared/analytics';
import type { GradeStats, MetricView } from '../api/learningQueries';

const insufficientReason = /n (\d+) of at least (\d+)/;
const noNumber = 'The server sent no number.';

/**
 * The server's figure as a kit state. A number is read only for `value` and `provisional`; any other state keeps its word and
 * reason, so no digit is drawn for it (P8-00 D3).
 */
export function metricStateOf(metric: MetricView): MetricState {
  const reason = metric.reason ?? undefined;
  switch (metric.state) {
    case 'value':
      return metric.number === null
        ? { kind: 'unavailable', reason: noNumber }
        : { kind: 'value', value: metric.number };
    case 'provisional':
      return metric.number === null
        ? { kind: 'unavailable', reason: noNumber }
        : { kind: 'provisional', value: metric.number, reason: reason ?? 'Provisional' };
    case 'insufficient': {
      const found = insufficientReason.exec(reason ?? '');
      return found?.[1] && found[2]
        ? { kind: 'insufficient', n: Number(found[1]), min: Number(found[2]) }
        : { kind: 'unavailable', reason: reason ?? 'Not enough data.' };
    }
    case 'not_evaluable':
      return { kind: 'not_evaluable', reason: reason ?? 'Not evaluable.' };
    case 'empty':
      return { kind: 'empty', reason };
    default:
      return { kind: 'unavailable', reason: reason ?? 'Not available.' };
  }
}

const noTrigger = 'No ticket triggered';

/** A mean of a decisions row: empty without a triggered ticket, provisional with the server's note, else a value. */
export function statsMeanState(row: GradeStats): MetricState {
  if (row.triggered === 0 || row.meanR === null) return { kind: 'empty', reason: noTrigger };
  return row.note ? { kind: 'provisional', value: row.meanR, reason: row.note } : { kind: 'value', value: row.meanR };
}

/** The win rate of a decisions row, in the same states as its mean. */
export function statsWinState(row: GradeStats): MetricState {
  if (row.triggered === 0 || row.winRate === null) return { kind: 'empty', reason: noTrigger };
  return row.note
    ? { kind: 'provisional', value: row.winRate, reason: row.note }
    : { kind: 'value', value: row.winRate };
}

/** MET / NOT MET / NOT EVALUABLE as the server prints them; anything else is not a pass. */
export function verdictOf(text: string): 'met' | 'not_met' | 'not_evaluable' {
  if (text === 'MET') return 'met';
  if (text === 'NOT MET') return 'not_met';
  return 'not_evaluable';
}
