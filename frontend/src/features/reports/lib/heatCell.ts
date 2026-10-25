import { formatCount, formatR, formatRate, formatRatio, type MetricState } from '@/shared/analytics';
import type { PivotMetric } from './pivotLimits';

const words: Record<string, string> = {
  provisional: 'provisional',
  insufficient: 'insufficient data',
  unavailable: 'not available',
  not_evaluable: 'not evaluable',
  empty: 'no records yet',
};

/** The state in words for a cell's accessible name; a number only for a value. */
export const stateText = (state: MetricState, format: (value: number) => string): string =>
  state.kind === 'value' ? format(state.value) : (words[state.kind] ?? state.kind);

export const metricFormat = (metric: PivotMetric): ((value: number) => string) => {
  switch (metric) {
    case 'win_rate':
      return formatRate;
    case 'profit_factor':
      return formatRatio;
    case 'trades':
      return formatCount;
    default:
      return formatR;
  }
};

/** Only the R metrics are shaded (symmetric around zero); the others stay plain so no colour implies a sign. */
export const isShaded = (metric: PivotMetric): boolean =>
  metric !== 'win_rate' && metric !== 'profit_factor' && metric !== 'trades' && metric !== 'max_drawdown_r';

/** The largest absolute figure among VALUE cells only: a masked, empty or unavailable cell never feeds the colour scale. */
export const peakOf = (cells: readonly { value: { state: string; number: number | null } }[]): number =>
  Math.max(0, ...cells.map((c) => (c.value.state === 'value' ? Math.abs(c.value.number ?? 0) : 0)));

/**
 * The shade of a value cell from the largest absolute value among VALUE cells only (a masked cell never feeds the scale).
 * Anything that is not a value has no shade.
 */
export function shadeOf(state: MetricState, peak: number): { tone: 'success' | 'error'; alpha: number } | undefined {
  return state.kind === 'value' && peak > 0
    ? { tone: state.value >= 0 ? 'success' : 'error', alpha: 0.08 + 0.3 * (Math.abs(state.value) / peak) }
    : undefined;
}
