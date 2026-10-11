import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import SyncIcon from '@mui/icons-material/Sync';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Stack, Typography } from '@mui/material';
import type { ReactElement } from 'react';
import type { SyncRun, SyncStatus } from '@/shared/api';
import { formatAgo, formatIstTimeAndDay } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { describeSchedule, summariseRun, type RunTone } from '../lib/syncText';

const toneLook: Record<RunTone, { icon: ReactElement; color: string }> = {
  success: { icon: <CheckCircleIcon fontSize="small" />, color: 'success.main' },
  error: { icon: <ErrorIcon fontSize="small" />, color: 'error.main' },
  warning: { icon: <WarningIcon fontSize="small" />, color: 'warning.main' },
  info: { icon: <SyncIcon fontSize="small" />, color: 'info.main' },
};

/** "Synced 5 min ago — 2 new, 1 closed" / "Skipped 2 h ago — The account is not connected to Dhan." */
function LastRun({ run, running }: { run: SyncRun | null; running: boolean }) {
  if (running) {
    const look = toneLook.info;
    return (
      <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', color: look.color }} role="status">
        {look.icon}
        <Typography sx={{ fontWeight: 600 }}>Sync in progress…</Typography>
      </Stack>
    );
  }
  if (!run) {
    return (
      <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', color: 'text.secondary' }}>
        <InfoIcon fontSize="small" aria-hidden />
        <Typography>Not synced yet</Typography>
      </Stack>
    );
  }
  const summary = summariseRun(run);
  const look = toneLook[summary.tone];
  const when = run.finishedAt ?? run.startedAt;
  return (
    <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center' }}>
      <Stack component="span" sx={{ color: look.color }} aria-hidden>
        {look.icon}
      </Stack>
      <Typography sx={numeric}>
        <strong>{summary.title}</strong> <time dateTime={when}>{formatAgo(when)}</time> — {summary.detail}
      </Typography>
    </Stack>
  );
}

interface SyncStatusLineProps {
  /** The account's own status; null when no single account is in play (the run then comes from `fallbackRun`). */
  status: SyncStatus | null;
  fallbackRun: SyncRun | null;
}

/** Last run, and for an account: the auto-refresh setting and when it runs next. */
export function SyncStatusLine({ status, fallbackRun }: SyncStatusLineProps) {
  return (
    <Stack spacing={0.25}>
      <LastRun run={status ? status.last : fallbackRun} running={status?.running ?? false} />
      {status && (
        <Typography variant="body2" color="text.secondary" sx={numeric}>
          {describeSchedule(status)}
          {status.nextRunAt && ` · next run ${formatIstTimeAndDay(status.nextRunAt)} IST`}
        </Typography>
      )}
    </Stack>
  );
}
