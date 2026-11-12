import { Alert, Button, Card, CardContent, LinearProgress, Stack, Typography } from '@mui/material';
import { formatDate, formatNumber } from '@/shared/lib/format';
import { isReplayActive, useCancelReplay, type ReplayRun } from '../../api/replayApi';

const stateText: Record<string, string> = {
  queued: 'Queued',
  running: 'Running',
  cancelling: 'Cancelling after the session in flight',
  completed: 'Finished',
  cancelled: 'Cancelled: the finished sessions are kept and the figures below are partial.',
  failed: 'Failed',
};

/** Where the run is: sessions done of total, the as-of session, and Cancel while it moves. */
export function ReplayProgressCard({ run }: { run: ReplayRun }) {
  const cancel = useCancelReplay(run.id);
  const { sessionsDone, sessionsTotal, asOfSession } = run.completeness;
  const active = isReplayActive(run.state);
  return (
    <Card component="section" aria-labelledby="replay-progress-title">
      <CardContent sx={{ display: 'grid', gap: 1.25 }}>
        <Typography id="replay-progress-title" variant="h2" component="h2">
          Progress
        </Typography>
        <Typography role="status">{stateText[run.state] ?? run.state}</Typography>
        <LinearProgress
          variant="determinate"
          value={sessionsTotal > 0 ? (sessionsDone / sessionsTotal) * 100 : 0}
          aria-label="Sessions replayed"
        />
        <Typography variant="body2">
          {`${formatNumber(sessionsDone)} of ${formatNumber(sessionsTotal)} sessions`}
          {asOfSession ? ` · as of ${formatDate(asOfSession)}` : ''}
        </Typography>
        {run.state === 'failed' && (
          <Alert severity="error">{`The replay failed: ${run.failReason ?? 'no reason was stored'}`}</Alert>
        )}
        {cancel.isError && <Alert severity="error">The replay could not be cancelled; it may have finished.</Alert>}
        {active && (
          <Stack direction="row">
            <Button
              variant="outlined"
              color="warning"
              disabled={cancel.isPending || run.state === 'cancelling'}
              onClick={() => {
                cancel.mutate();
              }}
              sx={{ minHeight: 44 }}
            >
              Cancel
            </Button>
          </Stack>
        )}
      </CardContent>
    </Card>
  );
}
