import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import SkippedIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import { Box, Stack, Typography } from '@mui/material';
import type { ServiceRun } from '@/shared/api';
import { formatIstDayAndTime } from '@/shared/lib/format';
import { runStatusLabel } from '../lib/labels';
import { TriggerChip } from './TriggerChip';

/** The latest run: when it ended (started, while it runs) in India time, what started it, and why it failed or was skipped. */
export function LastRun({ run }: { run: ServiceRun | null }) {
  if (run === null) {
    return (
      <Typography variant="subtitle1" color="text.secondary" sx={{ fontWeight: 600 }}>
        Never run
      </Typography>
    );
  }
  const at = run.finishedAt ?? run.startedAt;
  const failed = run.status === 'failed';
  const why = failed || run.status === 'skipped';
  const Icon = failed ? ErrorIcon : SkippedIcon;
  return (
    <Box sx={{ whiteSpace: 'normal', minWidth: 0 }}>
      <Stack direction="row" sx={{ gap: 0.75, alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography component="time" variant="subtitle1" dateTime={at} sx={{ fontWeight: 700 }}>
          {formatIstDayAndTime(at)}
        </Typography>
        <TriggerChip run={run} />
      </Stack>
      {why && (
        <Typography
          variant="caption"
          component="div"
          sx={{
            display: 'flex',
            gap: 0.5,
            alignItems: 'flex-start',
            color: failed ? 'error.main' : 'text.secondary',
            overflowWrap: 'anywhere',
          }}
        >
          <Icon aria-hidden sx={{ fontSize: 15, mt: '1px', flex: 'none' }} />
          <span>
            {runStatusLabel(run.status)}
            {run.message ? `: ${run.message}` : ''}
          </span>
        </Typography>
      )}
    </Box>
  );
}
