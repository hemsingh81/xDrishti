import { carriesNumber } from '@/shared/analytics';
import type { GroupView } from '../api/reportQueries';
import { metricFromApi } from './metricFromApi';

/** Lower bound first (a basket with one comes before one without), then trades: never alphabetical, never by the raw mean. */
export function byCredibleBound(a: GroupView, b: GroupView): number {
  const lower = (g: GroupView) => {
    const state = metricFromApi(g.expectancyLowerR);
    return carriesNumber(state) ? state.value : null;
  };
  const [x, y] = [lower(a), lower(b)];
  if (x !== null && y !== null && x !== y) return y - x;
  if ((x === null) !== (y === null)) return x === null ? 1 : -1;
  return b.n - a.n;
}
