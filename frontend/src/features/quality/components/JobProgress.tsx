import { Box, LinearProgress, Typography } from '@mui/material';
import type { DataJob } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { percentDone, progressOf, resultCounts } from '../lib/progress';

/**
 * The job in progress: what it is doing now, how far it is, and (while it checks) how the instruments came out so far.
 * A live region, so a screen reader hears the steps change. `inline` lays it out as one slim row (the page, under the
 * summary ribbon); the default stacks it (the Inspect drawer).
 */
export function JobProgress({ job, inline = false }: { job: DataJob; inline?: boolean }) {
  const progress = progressOf(job);
  const title = progress?.title ?? 'Starting';
  const quality = job.currentPhase === 'quality' ? (job.quality ?? null) : null;
  const determinate = progress !== null && progress.total > 0;

  const heading = (
    <Typography variant={inline ? 'body2' : 'body1'} sx={{ fontWeight: 600 }}>
      {title}
      {determinate ? `: ${formatNumber(progress.done)} of ${formatNumber(progress.total)} ${progress.unit}` : '…'}
    </Typography>
  );
  const bar = determinate ? (
    <LinearProgress
      variant="determinate"
      value={percentDone(progress.done, progress.total)}
      aria-label={`${title} progress`}
      sx={inline ? { flex: '1 1 120px', maxWidth: 320, borderRadius: 1 } : { my: 1, borderRadius: 1 }}
    />
  ) : (
    <LinearProgress
      aria-label={`${title} progress`}
      sx={inline ? { flex: '1 1 120px', maxWidth: 320, borderRadius: 1 } : { my: 1, borderRadius: 1 }}
    />
  );
  const so = quality && (
    <Typography variant="body2" color="text.secondary" sx={numeric}>
      So far: {resultCounts(quality)}
    </Typography>
  );
  const message = progress?.message && (
    <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
      {progress.message}
    </Typography>
  );

  if (inline) {
    return (
      <Box
        role="status"
        sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', columnGap: 2, rowGap: 0.5, mb: 1, px: 0.5 }}
      >
        {heading}
        {bar}
        {so}
        {message}
      </Box>
    );
  }
  return (
    <Box role="status">
      {heading}
      {bar}
      {so}
      {message}
    </Box>
  );
}
