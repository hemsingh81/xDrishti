import { DataTable, type Column } from '@/shared/analytics';
import type { components } from '@/shared/api/schema';
import { formatIstDateTime } from '@/shared/lib/format';

type Row = components['schemas']['EventFilterRowView'];

const columns: readonly Column<Row>[] = [
  { kind: 'text', id: 'at', header: 'When', render: (r) => formatIstDateTime(r.changedAt) },
  { kind: 'text', id: 'state', header: 'Filter', render: (r) => (r.enabled ? 'On' : 'Off') },
  { kind: 'text', id: 'n', header: 'Window N', render: (r) => String(r.resultsWithinDays) },
  { kind: 'text', id: 'approval', header: 'Approval', render: (r) => r.approval },
  {
    kind: 'text',
    id: 'by',
    header: 'Decided by',
    // An automatic decision is never worded as the owner's.
    render: (r) =>
      r.decidedBy === 'auto'
        ? `applied automatically from verdict ${r.verdictRef ?? 'not recorded'}`
        : `owner (${r.actor})`,
  },
  { kind: 'text', id: 'reason', header: 'Reason', render: (r) => r.reason },
];

/** The latest 50 decision rows, newest first. */
export function EventFilterHistory({ rows }: { rows: readonly Row[] }) {
  return (
    <DataTable
      label="Event filter history"
      columns={columns}
      rows={rows}
      rowKey={(r) => String(r.id)}
      status={
        rows.length === 0 ? { kind: 'empty', reason: 'the filter has never been switched (off by default)' } : undefined
      }
    />
  );
}
