import { Box, LinearProgress, Typography } from '@mui/material';
import { formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { percentDone } from '../lib/phases';

interface StepProgressProps {
  title: string;
  status: 'running' | 'pending';
  done: number;
  total: number;
  /** What is counted: "files", "instruments". */
  unit: string;
  message: string | null;
}

/** A running step's progress bar with its counts and what it is doing now; a step still queued says so. */
export function StepProgress({ title, status, done, total, unit, message }: StepProgressProps) {
  if (status === 'pending') {
    return (
      <Typography variant="body2" color="text.secondary">
        Waiting for the earlier steps
      </Typography>
    );
  }
  return (
    <Box>
      {total > 0 ? (
        <LinearProgress
          variant="determinate"
          value={percentDone(done, total)}
          aria-label={`${title} progress`}
          sx={{ my: 1, borderRadius: 1 }}
        />
      ) : (
        <LinearProgress aria-label={`${title} progress`} sx={{ my: 1, borderRadius: 1 }} />
      )}
      <Typography variant="body2" sx={{ fontWeight: 600, ...numeric }}>
        {total > 0 ? `${formatNumber(done)} of ${formatNumber(total)} ${unit}` : 'Starting…'}
      </Typography>
      {message && (
        <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
          {message}
        </Typography>
      )}
    </Box>
  );
}
