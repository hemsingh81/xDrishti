import { Box, Typography } from '@mui/material';
import { DataTable, formatCount, formatSigned, type Column } from '@/shared/analytics';
import type { PaperPhaseReport } from '../api/phaseQueries';

type Week = PaperPhaseReport['weeks'][number];
type Epoch = PaperPhaseReport['epochs'][number];

const count = (read: (w: Week) => number) => (w: Week) => formatCount(read(w));

const weekColumns: readonly Column<Week>[] = [
  { kind: 'text', id: 'week', header: 'Week', render: (w) => w.week },
  { kind: 'text', id: 'sessions', header: 'Sessions', align: 'right', render: count((w) => w.sessions) },
  { kind: 'text', id: 'plans', header: 'Plans', align: 'right', render: count((w) => w.plansPublished) },
  { kind: 'text', id: 'failed', header: 'Failed closed', align: 'right', render: count((w) => w.failedClosed) },
  { kind: 'text', id: 'manual', header: 'Manual runs', align: 'right', render: count((w) => w.manualRuns) },
  { kind: 'text', id: 'capture', header: 'Capture finished', align: 'right', render: count((w) => w.captureFinished) },
  { kind: 'text', id: 'counted', header: 'Counted', align: 'right', render: count((w) => w.tradesCounted) },
  { kind: 'text', id: 'cum', header: 'Cumulative', align: 'right', render: count((w) => w.cumulativeCounted) },
  { kind: 'text', id: 'cumR', header: 'Cumulative R', align: 'right', render: (w) => formatSigned(w.cumulativeR) },
  { kind: 'text', id: 'frozen', header: 'Frozen changes', align: 'right', render: count((w) => w.frozenChanges) },
  { kind: 'text', id: 'risk', header: 'Risk-down changes', align: 'right', render: count((w) => w.riskDownChanges) },
];

const epochColumns: readonly Column<Epoch>[] = [
  { kind: 'text', id: 'id', header: 'Epoch', render: (e) => `${e.id}${e.current ? ' (judged)' : ' (not judged)'}` },
  { kind: 'text', id: 'started', header: 'Started', render: (e) => e.startedOn },
  { kind: 'text', id: 'kind', header: 'Kind', render: (e) => e.kind },
  { kind: 'text', id: 'counted', header: 'Counted', align: 'right', render: (e) => formatCount(e.counted) },
  {
    kind: 'text',
    id: 'unexplained',
    header: 'Unexplained changes',
    align: 'right',
    render: (e) => formatCount(e.unexplainedChanges),
  },
  { kind: 'text', id: 'reason', header: 'Why', render: (e) => e.reason },
];

/** The week lines and the epoch history; earlier epochs are listed and never pooled into the judged one. */
export function PhaseTables({ report }: { report: PaperPhaseReport }) {
  return (
    <>
      <Box component="section" aria-labelledby="phase-weeks-heading">
        <Typography variant="h2" id="phase-weeks-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
          Week by week
        </Typography>
        <DataTable label="Weeks of the epoch" columns={weekColumns} rows={report.weeks} rowKey={(w) => w.week} />
      </Box>
      <Box component="section" aria-labelledby="phase-epochs-heading">
        <Typography variant="h2" id="phase-epochs-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
          Epochs
        </Typography>
        <DataTable label="Every epoch" columns={epochColumns} rows={report.epochs} rowKey={(e) => String(e.id)} />
      </Box>
    </>
  );
}
