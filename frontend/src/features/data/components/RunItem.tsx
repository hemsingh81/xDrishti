import { Box, Chip, Stack, Typography } from '@mui/material';
import type { DataJob } from '@/shared/api';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { triggerLabel } from '../lib/eod';
import { phaseKeys, stepsText } from '../lib/phases';
import { phaseReason } from '../lib/errors';
import { jobCounts } from '../lib/summaries';
import { JobWarnings } from './JobWarnings';
import { StepStatus } from './StepStatus';

/** What went wrong in a failed job: its own message, else the first failed step's. */
function failureOf(job: DataJob): string | null {
  if (job.status !== 'failed') return null;
  if (job.error) return job.error;
  const failed = phaseKeys.map((key) => job[key]).find((phase) => phase.status === 'failed');
  return failed ? phaseReason(failed.message, failed.code) : 'The update did not finish.';
}

/** Why a run did not run (skipped): the reason the scheduler recorded. */
function skipReasonOf(job: DataJob): string | null {
  return job.status === 'skipped' ? (job.error ?? 'No reason was recorded.') : null;
}

/** One run in the list: when it started, which steps, how it ended, the key counts and a failure's message. */
export function RunItem({ job }: { job: DataJob }) {
  const counts = jobCounts(job);
  const failure = failureOf(job);
  const skipReason = skipReasonOf(job);
  return (
    <Box
      component="li"
      sx={{ listStyle: 'none', py: 1, borderTop: 1, borderColor: 'divider', '&:first-of-type': { borderTop: 0 } }}
    >
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
        <Typography component="time" dateTime={job.startedAt} sx={{ fontWeight: 600 }}>
          {formatIstDateTime(job.startedAt)}
        </Typography>
        <Chip size="small" variant="outlined" label={triggerLabel(job)} />
        {skipReason === null && (
          <Typography variant="body2" color="text.secondary">
            {job.undoCsvFile ? `Undo of ${job.undoCsvFile}` : stepsText(job.steps)}
            {job.csvFiles && !job.undoCsvFile
              ? ` · ${formatNumber(job.csvFiles.length)} chosen CSV ${job.csvFiles.length === 1 ? 'file' : 'files'}${job.forceCsv ? ' (loaded again)' : ''}`
              : job.forceCsv
                ? ' · all CSV files again'
                : ''}
            {job.refetchGaps ? ' · gaps fetched again' : ''}
          </Typography>
        )}
        <StepStatus status={job.status} />
      </Stack>
      {counts.length > 0 && (
        <Typography variant="body2" color="text.secondary">
          {counts.join(' · ')}
        </Typography>
      )}
      {skipReason && (
        <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
          {skipReason}
        </Typography>
      )}
      <JobWarnings warnings={job.warnings} />
      {failure && (
        <Typography variant="body2" sx={{ color: 'error.main', overflowWrap: 'anywhere' }}>
          {failure}
        </Typography>
      )}
    </Box>
  );
}
