import { Box, Card, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { LoadingState } from '@/shared/ui';
import { jobsQuery, recentRunCount } from '../api/dataQueries';
import { problemText } from '../lib/errors';
import { QueryAlert } from './QueryAlert';
import { RunItem } from './RunItem';

/** The last runs, newest first, as a simple list: the trigger, the steps, how it ended, the key counts, a failure's message. */
export function RunsPanel() {
  const jobs = useQuery(jobsQuery);
  return (
    <Card sx={{ flex: '1 1 auto', minHeight: 0, overflowY: 'auto', px: 1.5, py: 1 }}>
      {jobs.isPending && <LoadingState label="Loading recent runs" />}
      {jobs.isError && (
        <QueryAlert
          message={problemText(jobs.error, 'The recent runs could not be loaded.')}
          onRetry={() => {
            void jobs.refetch();
          }}
        />
      )}
      {jobs.data?.length === 0 && <Typography color="text.secondary">No updates have run yet.</Typography>}
      {jobs.data && jobs.data.length > 0 && (
        <>
          <Typography variant="caption" color="text.secondary" component="p">
            The last {recentRunCount} runs, newest first.
          </Typography>
          <Box component="ul" aria-label="Recent runs" sx={{ m: 0, p: 0 }}>
            {jobs.data.map((job) => (
              <RunItem key={job.id} job={job} />
            ))}
          </Box>
        </>
      )}
    </Card>
  );
}
