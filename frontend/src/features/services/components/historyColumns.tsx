import { Box } from '@mui/material';
import type { ServiceRun } from '@/shared/api';
import { formatDuration, formatIstDayAndTime } from '@/shared/lib/format';
import type { GridColumn } from '@/shared/ui/grid';
import { RunStatusChip } from './RunStatusChip';
import { TriggerChip } from './TriggerChip';

/** How long a finished run took; "—" while it runs. */
const durationOf = (run: ServiceRun): string =>
  run.finishedAt === null ? '—' : formatDuration((Date.parse(run.finishedAt) - Date.parse(run.startedAt)) / 1000);

/** The columns of the History drawer: start (IST), trigger, status, duration and the message. */
export const historyColumns: readonly GridColumn<ServiceRun>[] = [
  {
    id: 'startedAt',
    header: 'Started',
    minWidth: 130,
    render: (run) => (
      <time dateTime={run.startedAt}>
        {formatIstDayAndTime(run.startedAt)}
        {run.attempt > 1 ? ` · attempt ${String(run.attempt)}` : ''}
      </time>
    ),
  },
  { id: 'trigger', header: 'Trigger', render: (run) => <TriggerChip run={run} /> },
  { id: 'status', header: 'Status', render: (run) => <RunStatusChip status={run.status} /> },
  { id: 'duration', header: 'Duration', numeric: true, accessor: durationOf },
  {
    id: 'message',
    header: 'Message',
    grow: true,
    minWidth: 200,
    render: (run) => <Box sx={{ whiteSpace: 'normal', overflowWrap: 'anywhere', py: 0.5 }}>{run.message ?? '—'}</Box>,
  },
];
