import { formatR } from '@/shared/analytics';
import { TimeSeriesChart } from '@/shared/ui/chart';
import type { ChartColor, ChartDash, ChartSeries } from '@/shared/ui/chart';
import type { DecisionsReport } from '../api/learningQueries';

const colors: readonly ChartColor[] = ['accent', 'info', 'warn', 'neutral'];
const dashes: readonly ChartDash[] = ['solid', 'dashed', 'dotted', 'solid'];

/** Cumulative R of the top-1, top-3, top-5 and all tickets; series differ by colour role and by dash. */
export function DecisionCurves({ curves }: { curves: DecisionsReport['curves'] }) {
  const series: ChartSeries[] = curves.map((curve, index) => ({
    id: curve.name,
    name: curve.name,
    color: colors[index % colors.length] ?? 'neutral',
    dash: dashes[index % dashes.length],
    data: curve.points.map((p) => [p.date, p.cumulativeR] as const),
  }));
  return (
    <TimeSeriesChart
      label="Cumulative R by rank, simulated on plan prices"
      series={series}
      valueFormatter={formatR}
      empty="No graded plan in the range yet."
      deferUntilVisible
    />
  );
}
