import { DataTable, formatCount, formatR, formatRate } from '@/shared/analytics';
import type { GradeStats } from '../api/learningQueries';
import { statsMeanState, statsWinState } from '../lib/metricView';

interface GradeStatsTableProps {
  label: string;
  firstHeader: string;
  rows: readonly GradeStats[];
}

/** One group of the decisions report. A row without a triggered ticket is empty; a thin row carries the server's note. */
export function GradeStatsTable({ label, firstHeader, rows }: GradeStatsTableProps) {
  return (
    <DataTable
      label={label}
      rows={rows}
      rowKey={(row) => row.name}
      columns={[
        { kind: 'text', id: 'name', header: firstHeader, render: (row) => row.name },
        { kind: 'text', id: 'tickets', header: 'Tickets', align: 'right', render: (row) => formatCount(row.tickets) },
        {
          kind: 'text',
          id: 'triggered',
          header: 'Triggered',
          align: 'right',
          render: (row) => formatCount(row.triggered),
        },
        { kind: 'metric', id: 'mean', header: 'Mean R', metric: statsMeanState, format: formatR },
        { kind: 'metric', id: 'win', header: 'Win rate', metric: statsWinState, format: formatRate },
      ]}
    />
  );
}
