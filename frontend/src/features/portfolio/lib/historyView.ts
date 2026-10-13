import type { ChartSeries } from '@/shared/ui/chart';
import type { PortfolioHistory } from '@/shared/api';
import { benchmarkSeriesName } from './benchmark';

/**
 * The lines of the history chart: Value, Invested and, when the benchmark has figures for the series, the same cash flows
 * in the index. The lines differ in style as well as colour (solid, dashed, dotted).
 */
export function historySeries(history: PortfolioHistory): ChartSeries[] {
  const series: ChartSeries[] = [
    { id: 'value', name: 'Value', color: 'accent', data: history.points.map((p) => [p.date, p.value]) },
    {
      id: 'invested',
      name: 'Invested',
      color: 'neutral',
      dash: 'dashed',
      data: history.points.map((p) => [p.date, p.invested]),
    },
  ];
  if (history.benchmark.state === 'ok' && history.points.some((p) => p.benchmarkValue !== null)) {
    series.push({
      id: 'benchmark',
      name: benchmarkSeriesName(history.benchmark),
      color: 'warn',
      dash: 'dotted',
      data: history.points.map((p) => [p.date, p.benchmarkValue]),
    });
  }
  return series;
}
