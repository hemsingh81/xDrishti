import { Typography } from '@mui/material';
import type { QualityRun } from '@/shared/api';
import { formatIstDayAndTime } from '@/shared/lib/format';
import { runStatusLabel, triggerLabel } from '../lib/labels';

/** "Last run 12 Oct 15:52 · Automatic · Succeeded"; a failed run adds why. Said in words, not by colour. */
export function LastRunLine({ run }: { run: QualityRun }) {
  const at = run.finishedAt ?? run.startedAt;
  return (
    <Typography variant="body2" color="text.secondary" sx={{ minWidth: 0, overflowWrap: 'anywhere' }}>
      Last run <time dateTime={at}>{formatIstDayAndTime(at, { weekday: false })}</time> · {triggerLabel(run)} ·{' '}
      {runStatusLabel(run.status)}
      {run.status === 'failed' && run.error ? ` — ${run.error}` : ''}
    </Typography>
  );
}
