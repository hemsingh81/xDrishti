import { Alert, Button, CircularProgress, Stack } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { isActive, pollMs, runKeys, runQuery } from '../api/runQueries';

/**
 * The queued hold-out look (CF-06 part C): follows the job until it ends. While it is queued or running the card says so; when it
 * ends, in any way, the report and the ledger are read again, which shows the stored numbers if the look was stored; when it fails or stops the reason is said and the owner may ask again.
 */
export function HoldOutJobStatus({ jobId, onDismiss }: { jobId: string; onDismiss: () => void }) {
  const queryClient = useQueryClient();
  const job = useQuery(runQuery(jobId, pollMs));
  const state = job.data?.state;
  useEffect(() => {
    if (state !== undefined && !isActive(state)) {
      void queryClient.invalidateQueries({ queryKey: runKeys.reports });
      void queryClient.invalidateQueries({ queryKey: ['lab', 'ledger'] });
    }
  }, [state, queryClient]);

  if (job.isError) {
    return (
      <Alert severity="error" action={<Button onClick={onDismiss}>Dismiss</Button>} sx={{ my: 1 }}>
        The progress of the hold-out look could not be read. Open the report again to see whether it was stored.
      </Alert>
    );
  }
  if (job.data === undefined || isActive(job.data.state) || job.data.state === 'succeeded') {
    return (
      <Stack role="status" direction="row" spacing={1} sx={{ alignItems: 'center', my: 1 }}>
        <CircularProgress size={16} />
        <span>
          {job.data?.state === 'succeeded'
            ? 'The hold-out look is stored. Reading the report...'
            : job.data?.state === 'running'
              ? 'The hold-out look is running.'
              : 'The hold-out look is queued.'}
        </span>
      </Stack>
    );
  }
  const why = job.data.problems[0]?.message ?? job.data.error ?? job.data.state;
  // an interrupted job, or one that found the look already taken, may have stored the look: do not claim otherwise
  const unsure = job.data.state === 'interrupted' || job.data.error === 'lab.holdout_used';
  return (
    <Alert severity="error" action={<Button onClick={onDismiss}>Try again</Button>} sx={{ my: 1 }}>
      {`The hold-out look did not complete (${job.data.state}). ${unsure ? 'Open the report to see whether the look was stored.' : 'Nothing was stored.'} ${why}`}
    </Alert>
  );
}
