import { DataTable, formatAsOf, formatRate } from '@/shared/analytics';
import type { AutonomySettings } from '../api/learningQueries';

const onOff = (on: boolean) => (on ? 'ON' : 'OFF (manual)');

/** Who changed the autonomy switches, when, why, and the values set. Read only. */
export function AutonomyHistory({ history }: { history: AutonomySettings['history'] }) {
  return (
    <DataTable
      label="Autonomy change history"
      rows={history}
      rowKey={(row) => String(row.id)}
      columns={[
        { kind: 'text', id: 'at', header: 'When', render: (row) => formatAsOf(row.changedAt) },
        { kind: 'text', id: 'who', header: 'Who', render: (row) => row.actor },
        { kind: 'text', id: 'reason', header: 'Reason', render: (row) => row.reason },
        { kind: 'text', id: 'auto', header: 'Automatic approval', render: (row) => onOff(row.autoApproveTickets) },
        { kind: 'text', id: 'promote', header: 'Reserve promotion', render: (row) => onOff(row.promoteFromReserve) },
        { kind: 'text', id: 'bound', header: 'Minimum win-rate bound', render: (row) => formatRate(row.minPWin) },
        { kind: 'text', id: 'cap', header: 'Daily cap', render: (row) => String(row.maxAutoPerDay) },
      ]}
    />
  );
}
