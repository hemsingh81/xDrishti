import { DataTable, formatAsOfDay } from '@/shared/analytics';
import type { PlanPhaseCheck } from '../api/learningQueries';

/** The last ten trading sessions of the nightly plan, as the P7 check read them. */
export function SessionsTable({ sessions }: { sessions: PlanPhaseCheck['report']['sessions'] }) {
  return (
    <DataTable
      label="Last ten sessions"
      rows={sessions}
      rowKey={(row) => row.date}
      columns={[
        { kind: 'text', id: 'date', header: 'Session', render: (row) => formatAsOfDay(row.date) },
        { kind: 'text', id: 'state', header: 'State', render: (row) => row.state },
        {
          kind: 'text',
          id: 'runs',
          header: 'Scheduled / manual / failed',
          render: (row) => `${row.scheduledRuns} / ${row.manualRuns} / ${row.failedRuns}`,
        },
        {
          kind: 'text',
          id: 'tickets',
          header: 'Primary / reserve / rejected',
          render: (row) =>
            row.published ? `${row.primary ?? 0} / ${row.reserve ?? 0} / ${row.rejected ?? 0}` : 'no plan',
        },
        { kind: 'text', id: 'gates', header: 'Gates', render: (row) => row.gates },
        { kind: 'text', id: 'note', header: 'Note', render: (row) => row.note },
      ]}
    />
  );
}
