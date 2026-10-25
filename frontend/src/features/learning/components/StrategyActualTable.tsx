import { DataTable, formatCount, formatR } from '@/shared/analytics';
import type { PlanVsActual } from '../api/learningQueries';
import { metricStateOf } from '../lib/metricView';

type Row = PlanVsActual['byStrategy'][number];

/** Plan R, realised R and entry slippage per strategy; below the minimum every figure is insufficient. */
export function StrategyActualTable({ rows }: { rows: readonly Row[] }) {
  return (
    <DataTable
      label="Plan versus actual by strategy"
      rows={rows}
      rowKey={(row) => row.strategyId}
      status={rows.length === 0 ? { kind: 'empty', reason: 'No ticket has been graded on fills yet' } : undefined}
      columns={[
        { kind: 'text', id: 'strategy', header: 'Strategy', render: (row) => row.strategyId },
        { kind: 'text', id: 'n', header: 'Tickets', align: 'right', render: (row) => formatCount(row.n) },
        {
          kind: 'metric',
          id: 'plan',
          header: 'Plan R (mean)',
          metric: (row) => metricStateOf(row.planR),
          format: formatR,
        },
        {
          kind: 'metric',
          id: 'actual',
          header: 'Actual R (mean)',
          metric: (row) => metricStateOf(row.actualR),
          format: formatR,
        },
        {
          kind: 'metric',
          id: 'slip',
          header: 'Entry slippage (mean, positive is worse)',
          metric: (row) => metricStateOf(row.entrySlippageR),
          format: formatR,
        },
      ]}
    />
  );
}
