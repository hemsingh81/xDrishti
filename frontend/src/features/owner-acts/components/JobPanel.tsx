import { Alert, Button, LinearProgress, Paper, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { isFinal, jobQuery, useCancelJob } from '../api/experimentApi';
import { useExperimentEvents } from '../hooks/useExperimentEvents';
import { ResultCard } from './ResultCard';
import { startProblem } from './problemText';

/** Polling when the hub is not carrying the events (as the Lab does, only faster: a job is short and watched). */
export const pollMs = 2000;

const stateWords: Record<string, string> = {
  queued: 'Waiting to start',
  running: 'Running',
  succeeded: 'Finished',
  failed: 'Failed',
  cancelled: 'Cancelled',
  interrupted: 'Interrupted',
};

/** One job: progress while it runs (hub events, else polling), a Cancel button, the result card when it is over. */
export function JobPanel({ id }: { id: string }) {
  const { live } = useExperimentEvents(id);
  const query = useQuery(jobQuery(id, live ? false : pollMs));
  const cancel = useCancelJob();
  const job = query.data;

  return (
    <Paper variant="outlined" component="section" aria-label="Replay job" sx={{ p: 2 }}>
      <Stack spacing={1.5}>
        {query.isPending && <LoadingState label="Loading the job…" />}
        {query.isError && <ErrorState error={query.error} />}
        {job && (
          <>
            <Typography variant="h3" component="h2">
              {job.knob.name}: {job.knob.baselineValue} to {job.knob.candidateValue}
            </Typography>
            <Typography>
              State: {stateWords[job.state] ?? job.state}
              {job.stage ? `, stage ${job.stage}` : ''}
              {job.cancelRequested && !isFinal(job.state) ? ', stopping' : ''}
            </Typography>
            {!isFinal(job.state) && (
              <>
                <LinearProgress
                  variant="determinate"
                  value={job.total > 0 ? Math.min(100, (job.done / job.total) * 100) : 0}
                  aria-label="Replay progress"
                />
                <Typography variant="body2" color="text.secondary">
                  {job.done} of {job.total}
                </Typography>
                {cancel.isError && (
                  <Alert severity="error" role="alert">
                    {startProblem(cancel.error).text}
                  </Alert>
                )}
                <div>
                  <Button
                    variant="outlined"
                    disabled={cancel.isPending || job.cancelRequested}
                    onClick={() => {
                      cancel.mutate(job.id);
                    }}
                  >
                    Cancel
                  </Button>
                </div>
              </>
            )}
            {isFinal(job.state) && <ResultCard job={job} />}
          </>
        )}
      </Stack>
    </Paper>
  );
}
