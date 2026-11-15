import { Alert, Box, Card, CardContent, Typography } from '@mui/material';
import { formatDate, formatInr } from '@/shared/lib/format';
import { StatTile } from '@/shared/ui';
import type { SimDays } from '../../api/simDaysApi';
import { waitingLabel } from '../../lib/simDaysForm';
import { HindsightBanner } from '../replay/HindsightBanner';

/** The run at a glance: the cursor day, the phase, the equity, the days done, why it waits and its labels. */
export function SimDaysHeader({ run, equity }: { run: SimDays; equity: number }) {
  return (
    <Card component="section" aria-labelledby="sim-days-status-title">
      <CardContent sx={{ display: 'grid', gap: 1.5, minWidth: 0 }}>
        <Typography id="sim-days-status-title" variant="h2" component="h2">
          {`Simulation ${formatDate(run.start)} to ${formatDate(run.end)} · ${run.mode}`}
        </Typography>
        <Box
          sx={{
            display: 'grid',
            gap: 1.5,
            gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, 1fr)' },
          }}
        >
          <StatTile label="Day" value={formatDate(run.cursor)} />
          <StatTile label="Phase" value={run.auto ? `${run.phase} · auto` : run.phase} />
          <StatTile label="Equity" value={formatInr(equity)} />
          <StatTile label="Days done" value={`${run.daysDone} of ${run.daysTotal}`} />
        </Box>
        {run.waiting && (
          <Typography role="status" color="text.secondary">
            {waitingLabel(run.waiting)}
          </Typography>
        )}
        {run.failReason && <Alert severity="error">{run.failReason}</Alert>}
        {run.holdOutNote && (
          <Alert severity="info" role="note" aria-label="Hold-out">
            {run.holdOutNote}
          </Alert>
        )}
        <HindsightBanner run={run} />
      </CardContent>
    </Card>
  );
}
