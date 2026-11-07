import { DataTable, NonValue } from '@/shared/analytics';
import type { ScorecardRow } from '../api/readinessQueries';
import { rowStatus } from '../lib/words';
import { RowStatusChip } from './StatusChips';

/** The 15-row edge checklist: the input each row was judged on, or why it could not be. */
export function ChecklistTable({ rows }: { rows: readonly ScorecardRow[] }) {
  return (
    <DataTable
      label="The 15-row edge checklist"
      rows={rows}
      rowKey={(row) => String(row.number)}
      status={rows.length === 0 ? { kind: 'empty', reason: 'The server returned no checklist row.' } : undefined}
      columns={[
        { kind: 'text', id: 'number', header: 'Row', render: (row) => String(row.number), align: 'right' },
        { kind: 'text', id: 'name', header: 'Criterion', render: (row) => row.name },
        { kind: 'text', id: 'status', header: 'Status', render: (row) => <RowStatusChip status={row.status} /> },
        {
          kind: 'text',
          id: 'value',
          header: 'Input',
          // A row that is not available shows the word and the server's reason, never a figure.
          render: (row) =>
            rowStatus(row.status) === 'unavailable' ? (
              <NonValue state={{ kind: 'unavailable', reason: row.value }} />
            ) : (
              row.value
            ),
        },
        { kind: 'text', id: 'threshold', header: 'Go threshold', render: (row) => row.threshold },
        { kind: 'text', id: 'producer', header: 'Producer', render: (row) => row.producer },
      ]}
    />
  );
}
