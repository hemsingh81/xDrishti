import { Alert, Box, Button, Card, CardContent, LinearProgress, Stack, Typography } from '@mui/material';
import { formatCount } from '@/shared/analytics';
import { formatBytes } from './formatBytes';
import { formatDuration, formatIstDateTime } from '@/shared/lib/format';
import { isActive, useSimAction, type SimAction, type SimJob } from '../api/simulationApi';

const pretty = (text: string) => text.replace(/_/g, ' ');

function Estimate({ job }: { job: SimJob }) {
  const e = job.estimate;
  if (!e) {
    return (
      <Typography variant="body2" color="text.secondary">
        Estimate: measured after 1 % of the chunks are done; until then there is no time or disk figure.
      </Typography>
    );
  }
  return (
    <Typography variant="body2" color="text.secondary">
      {`Estimate (${String(e.sampledChunks)} chunks measured, ${e.basis}): about ${formatDuration(e.remainingSeconds)} left and ${formatBytes(e.remainingStoredBytes)} more to store.`}
    </Typography>
  );
}

/** The job's progress and its controls. Pause and cancel are for a running job, resume for a paused one, start when none is active. */
export function SimJobPanel({ job }: { job: SimJob | null }) {
  const act = useSimAction();
  const run = (action: SimAction) => {
    act.mutate(action);
  };
  const active = isActive(job);
  const percent = job && job.chunksTotal > 0 ? Math.round((job.chunksDone / job.chunksTotal) * 100) : 0;
  const button = (action: SimAction, label: string, variant: 'contained' | 'outlined' = 'outlined') => (
    <Button
      variant={variant}
      size="small"
      disabled={act.isPending}
      onClick={() => {
        run(action);
      }}
      sx={{ minHeight: 44 }}
    >
      {label}
    </Button>
  );
  return (
    <Card component="section" aria-labelledby="sim-job-title">
      <CardContent>
        <Typography id="sim-job-title" variant="h2" component="h2" sx={{ mb: 1.5 }}>
          Simulation job
        </Typography>
        <Stack sx={{ gap: 1.5 }}>
          {job ? (
            <Box>
              <Typography sx={{ mb: 0.5 }}>
                {`State: ${pretty(job.state)}${job.pauseReason ? ` (${pretty(job.pauseReason)})` : ''}`}
              </Typography>
              <LinearProgress
                variant="determinate"
                value={percent}
                aria-label="Job progress"
                sx={{ height: 8, borderRadius: 1 }}
              />
              <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                {`${formatCount(job.chunksDone)} of ${formatCount(job.chunksTotal)} chunks done (${String(percent)} %), ${formatCount(job.chunksRunning)} running, ${formatCount(job.chunksRemaining)} remaining, ${formatCount(job.chunksFailed)} failed. Updated ${formatIstDateTime(job.updatedAt)}.`}
              </Typography>
            </Box>
          ) : (
            <Typography color="text.secondary">
              No simulation job yet. Starting one runs every stored strategy over every tracked stock and writes
              training runs only.
            </Typography>
          )}
          {job && <Estimate job={job} />}
          {act.isError && <Alert severity="error">{act.error.message}</Alert>}
          <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
            {!active && button('start', 'Start simulation', 'contained')}
            {job?.state === 'running' && button('pause', 'Pause')}
            {job?.state === 'paused' && button('resume', 'Resume', 'contained')}
            {active && button('cancel', 'Cancel')}
          </Stack>
        </Stack>
      </CardContent>
    </Card>
  );
}
