import { Box, LinearProgress, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { dataJobQuery } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { percentDone, progressOf } from '../lib/jobProgress';
import { servicesRunningPollMs } from '../lib/polling';

/**
 * The progress of the data job a running service started (the end-of-day run): what it does now and how far it is,
 * the same job the Data screen shows. Read every couple of seconds while it runs; nothing once it is over.
 */
export function RunProgress({ jobId }: { jobId: string }) {
  const job = useQuery({
    ...dataJobQuery(jobId),
    refetchInterval: (query) => (query.state.data?.status === 'running' ? servicesRunningPollMs : false),
  });
  if (job.data?.status !== 'running') return null;
  const progress = progressOf(job.data);
  const title = progress?.title ?? 'Starting';
  const determinate = progress !== null && progress.total > 0;
  return (
    <Box role="status" sx={{ minWidth: 160, whiteSpace: 'normal' }}>
      <Typography variant="caption" component="div" sx={{ fontWeight: 600, ...numeric }}>
        {title}
        {determinate ? `: ${formatNumber(progress.done)} of ${formatNumber(progress.total)} ${progress.unit}` : '…'}
      </Typography>
      <LinearProgress
        variant={determinate ? 'determinate' : 'indeterminate'}
        value={determinate ? percentDone(progress.done, progress.total) : undefined}
        aria-label={`${title} progress`}
        sx={{ mt: 0.5, borderRadius: 1 }}
      />
    </Box>
  );
}
