import { Box, Stack, Typography } from '@mui/material';
import type { DataJob } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import { phaseKeys, stepsText } from '../lib/phases';
import { phaseReason } from '../lib/errors';
import { jobCounts } from '../lib/summaries';
import { StepStatus } from './StepStatus';

/** What went wrong in a failed job: its own message, else the first failed step's. */
function failureOf(job: DataJob): string | null {
  if (job.status !== 'failed') return null;
  if (job.error) return job.error;
  const failed = phaseKeys.map((key) => job[key]).find((phase) => phase.status === 'failed');
  return failed ? phaseReason(failed.message, failed.code) : 'The update did not finish.';
}

/** One run in the list: when it started, which steps, how it ended, the key counts and a failure's message. */
export function RunItem({ job }: { job: DataJob }) {
  const counts = jobCounts(job);
  const failure = failureOf(job);
  return (
    <Box
      component="li"
      sx={{ listStyle: 'none', py: 1.25, borderTop: 1, borderColor: 'divider', '&:first-of-type': { borderTop: 0 } }}
    >
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
        <Typography component="time" dateTime={job.startedAt} sx={{ fontWeight: 600 }}>
          {formatIstDateTime(job.startedAt)}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {stepsText(job.steps)}
          {job.forceCsv ? ' · all CSV files again' : ''}
        </Typography>
        <StepStatus status={job.status} />
      </Stack>
      {counts.length > 0 && (
        <Typography variant="body2" color="text.secondary">
          {counts.join(' · ')}
        </Typography>
      )}
      {failure && (
        <Typography variant="body2" sx={{ color: 'error.main', overflowWrap: 'anywhere' }}>
          {failure}
        </Typography>
      )}
    </Box>
  );
}
