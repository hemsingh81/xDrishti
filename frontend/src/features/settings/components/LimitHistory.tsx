import { DataTable, type Column } from '@/shared/analytics';
import { formatIstDateTime } from '@/shared/lib/format';

export interface LimitHistoryRow {
  id: number;
  /** What changed: a setting key, a basket name or the automatic dismissal. */
  what: string;
  from: string;
  to: string;
  /** The direction as the server stored it (tighten, loosen, neutral); empty where the row has none. */
  direction: string;
  actor: string;
  changedAt: string;
}

const columns: readonly Column<LimitHistoryRow>[] = [
  { kind: 'text', id: 'at', header: 'When', render: (r) => formatIstDateTime(r.changedAt) },
  { kind: 'text', id: 'what', header: 'Setting', render: (r) => r.what },
  { kind: 'text', id: 'from', header: 'From', render: (r) => r.from },
  { kind: 'text', id: 'to', header: 'To', render: (r) => r.to },
  { kind: 'text', id: 'direction', header: 'Direction', render: (r) => r.direction || 'not applicable' },
  { kind: 'text', id: 'by', header: 'By', render: (r) => r.actor },
];

/** The latest changes of one limit setting, newest first (read only). */
export function LimitHistory({ label, rows }: { label: string; rows: readonly LimitHistoryRow[] }) {
  return (
    <DataTable
      label={label}
      columns={columns}
      rows={rows}
      rowKey={(r) => String(r.id)}
      status={
        rows.length === 0
          ? { kind: 'empty', reason: 'nothing has been changed yet (everything is at its default)' }
          : undefined
      }
    />
  );
}
