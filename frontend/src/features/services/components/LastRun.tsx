import { Box, Stack, Typography } from '@mui/material';
import type { ServiceRun } from '@/shared/api';
import { formatIstDayAndTime } from '@/shared/lib/format';
import { runStatusLabel } from '../lib/labels';
import { TriggerChip } from './TriggerChip';

/** The latest run: when it ended (started, while it runs) in India time, what started it, and why it failed or was skipped. */
export function LastRun({ run }: { run: ServiceRun | null }) {
  if (run === null) {
    return (
      <Typography variant="body2" color="text.secondary">
        Never run
      </Typography>
    );
  }
  const at = run.finishedAt ?? run.startedAt;
  const why = run.status === 'failed' || run.status === 'skipped';
  return (
    <Box sx={{ whiteSpace: 'normal', minWidth: 0, maxWidth: 260 }}>
      <Stack direction="row" sx={{ gap: 0.75, alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography component="time" variant="body2" dateTime={at}>
          {formatIstDayAndTime(at)}
        </Typography>
        <TriggerChip run={run} />
      </Stack>
      {why && (
        <Typography
          variant="caption"
          component="div"
          sx={{ color: run.status === 'failed' ? 'error.main' : 'text.secondary', overflowWrap: 'anywhere' }}
        >
          {runStatusLabel(run.status)}
          {run.message ? `: ${run.message}` : ''}
        </Typography>
      )}
    </Box>
  );
}
