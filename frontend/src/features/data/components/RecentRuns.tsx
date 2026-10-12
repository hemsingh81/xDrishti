import { Box, Card, CardContent, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useId } from 'react';
import { LoadingState } from '@/shared/ui';
import { jobsQuery } from '../api/dataQueries';
import { problemText } from '../lib/errors';
import { QueryAlert } from './QueryAlert';
import { RunItem } from './RunItem';

/** The last few runs, newest first: compact, with a failed run's message. */
export function RecentRuns() {
  const titleId = useId();
  const jobs = useQuery(jobsQuery);
  return (
    <Card component="section" aria-labelledby={titleId}>
      <CardContent>
        <Typography id={titleId} variant="h2" sx={{ mb: 1 }}>
          Recent runs
        </Typography>
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
          <Box component="ul" aria-label="Recent runs" sx={{ m: 0, p: 0 }}>
            {jobs.data.map((job) => (
              <RunItem key={job.id} job={job} />
            ))}
          </Box>
        )}
      </CardContent>
    </Card>
  );
}
