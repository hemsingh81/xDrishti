import type { MetricState } from '@/shared/analytics';
import type { Schemas } from '@/shared/api';

export type Figure = Schemas['MetricView'];

const sample = /n = (\d+), at least (\d+)/;

/**
 * The kit state of a server figure (BK-07). A `value` or `provisional` without a number is `unavailable`, never 0. An
 * `insufficient` figure carries its n and minimum in the server's reason ("n = 4, at least 5 needed"): they are read from it,
 * and without them the figure prints the reason as words. A state this client does not know is `unavailable`.
 */
export function figure(view: Figure): MetricState {
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
    case 'insufficient': {
      const found = sample.exec(reason ?? '');
      return found
        ? { kind: 'insufficient', n: Number(found[1]), min: Number(found[2]) }
        : { kind: 'not_evaluable', reason: reason ?? 'Too few observations.' };
    }
    case 'not_evaluable':
      return { kind: 'not_evaluable', reason: reason ?? 'The figure cannot be evaluated.' };
    case 'empty':
      return { kind: 'empty', reason };
    default:
      return { kind: 'unavailable', reason: reason ?? 'The input does not exist.' };
  }
}

/** True when the figure is a real number to draw. */
export const hasNumber = (view: Figure): boolean =>
  (view.state === 'value' || view.state === 'provisional') && typeof view.number === 'number';

/** The state of a figure that is drawn as words only (a panel with nothing to plot): a stray value there reads as "no records". */
export function nonValueOf(view: Figure): Exclude<MetricState, { kind: 'value' }> {
  const state = figure(view);
  return state.kind === 'value' ? { kind: 'empty' } : state;
}
