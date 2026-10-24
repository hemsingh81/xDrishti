/**
 * The honest states of a metric (docs/specs/P8-01, P8-00 D3). A metric is exactly one of these. Only `value` and
 * `provisional` carry a number; every other kind has no `value` field, so a screen cannot draw a digit for a non-value by
 * accident (no zero fill, no 0 % bar). The server's states (`insufficient`, `unavailable`, `not_evaluable`, `empty`) map
 * one to one; `loading` and `error` are the query's own states, shown in the same vocabulary.
 */
export type MetricState =
  /** A number the evidence supports. */
  | { kind: 'value'; value: number }
  /** A number shown with a caveat (partial data, a provisional basis); always printed with the word and the reason. */
  | { kind: 'provisional'; value: number; reason: string }
  /** Fewer observations than the stated minimum: no number is drawn. */
  | { kind: 'insufficient'; n: number; min: number }
  /** An input does not exist (regime, events, fills); `reason` says which. */
  | { kind: 'unavailable'; reason: string }
  /** The rule needs more than exists (six folds, a champion model); `reason` says what is missing. */
  | { kind: 'not_evaluable'; reason: string }
  /** No records yet. */
  | { kind: 'empty'; reason?: string }
  | { kind: 'loading' }
  | { kind: 'error'; reason?: string };

export type MetricKind = MetricState['kind'];

/** Kinds that carry no number, in the order the kit documents them. */
export const nonValueKinds = [
  'insufficient',
  'unavailable',
  'not_evaluable',
  'empty',
  'loading',
  'error',
] as const satisfies readonly MetricKind[];

type WithNumber = Extract<MetricState, { value: number }>;

export function carriesNumber(state: MetricState): state is WithNumber {
  return state.kind === 'value' || state.kind === 'provisional';
}

/**
 * The state to draw. A `value` or `provisional` whose number is not finite (NaN, infinity: a division by zero upstream)
 * is not a value: it becomes `unavailable`, never "NaN" and never 0.
 */
export function resolveMetric(state: MetricState): MetricState {
  if (carriesNumber(state) && !Number.isFinite(state.value)) {
    return { kind: 'unavailable', reason: 'The figure is not a finite number.' };
  }
  return state;
}

/** A number the API may omit: `null` / `undefined` / non-finite is `unavailable` (with the reason), never a zero. */
export function metricOf(value: number | null | undefined, missing = 'The API did not report a value.'): MetricState {
  return typeof value === 'number' && Number.isFinite(value)
    ? { kind: 'value', value }
    : { kind: 'unavailable', reason: missing };
}
