import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import SyncIcon from '@mui/icons-material/Sync';
import { Box, Chip, Stack, Typography } from '@mui/material';
import type { QualityRun } from '@/shared/api';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { runStatusLabel, triggerLabel } from '../lib/labels';

const looks = {
  succeeded: { color: 'success', icon: <CheckCircleIcon /> },
  failed: { color: 'error', icon: <ErrorIcon /> },
  running: { color: 'info', icon: <SyncIcon /> },
} as const;

const plural = (count: number, one: string, many: string) => `${formatNumber(count)} ${count === 1 ? one : many}`;

/** One run: its time, trigger, what it checked, the result counts and score, its state and a failure's message. */
export function RunItem({ run }: { run: QualityRun }) {
  const look = run.status in looks ? looks[run.status as keyof typeof looks] : undefined;
  return (
    <Box
      component="li"
      sx={{ listStyle: 'none', py: 1.25, borderTop: 1, borderColor: 'divider', '&:first-of-type': { borderTop: 0 } }}
    >
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
        <Typography component="time" dateTime={run.startedAt} sx={{ fontWeight: 600 }}>
          {formatIstDateTime(run.startedAt)}
        </Typography>
        <Chip size="small" variant="outlined" label={triggerLabel(run)} />
        <Chip
          size="small"
          variant="outlined"
          color={look?.color ?? 'default'}
          icon={look?.icon}
          label={runStatusLabel(run.status)}
        />
      </Stack>
      <Typography variant="body2" color="text.secondary">
        {run.instrumentId ? 'One instrument' : plural(run.instruments, 'instrument', 'instruments')} ·{' '}
        {formatNumber(run.passed)} passed · {plural(run.warned, 'warning', 'warnings')} · {formatNumber(run.failed)}{' '}
        failed
        {run.score === null ? '' : ` · score ${formatNumber(Math.round(run.score))}`} ·{' '}
        {plural(run.sessions, 'session', 'sessions')}
      </Typography>
      {run.error && (
        <Typography variant="body2" sx={{ color: 'error.main', overflowWrap: 'anywhere' }}>
          {run.error}
        </Typography>
      )}
    </Box>
  );
}
