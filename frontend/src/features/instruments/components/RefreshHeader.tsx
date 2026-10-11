import SyncIcon from '@mui/icons-material/Sync';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Button, Chip, Link, Typography } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import type { ReactElement, Ref } from 'react';
import type { RefreshStatus } from '@/shared/api';
import { formatAgo, formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { problemText } from '../lib/errors';
import { RefreshActions } from './RefreshActions';

interface RefreshHeaderProps {
  status: UseQueryResult<RefreshStatus>;
  busy: boolean;
  panelOpen: boolean;
  panelId: string;
  /** The control that was used, so closing the panel can return focus to it. */
  onOpenPanel: (opener: HTMLElement) => void;
  refreshButtonRef: Ref<HTMLButtonElement>;
  onStart: (force: boolean) => void;
}

interface ChipLook {
  label: string;
  color: 'info' | 'error' | 'warning';
  icon: ReactElement;
}

/** The one thing worth a chip: a run in progress, else a failed or warned last run. Nothing when all is well. */
function chipFor(data: RefreshStatus): ChipLook | null {
  if (data.current) return { label: 'Refreshing…', color: 'info', icon: <SyncIcon /> };
  if (data.last?.status === 'failed') return { label: 'Last refresh failed', color: 'error', icon: <ErrorIcon /> };
  if (data.last && data.last.warnings.length > 0) {
    return { label: 'Last refresh has warnings', color: 'warning', icon: <WarningIcon /> };
  }
  return null;
}

/**
 * The refresh controls in the page header: "Last refreshed 6 min ago · 4,381 active" (opens the details), a chip for a
 * running / failed / warned refresh, and the Refresh master list button. The details panel is not part of this.
 */
export function RefreshHeader({
  status,
  busy,
  panelOpen,
  panelId,
  onOpenPanel,
  refreshButtonRef,
  onStart,
}: RefreshHeaderProps) {
  const { data } = status;
  const chip = data ? chipFor(data) : null;
  const run = data?.last ?? null;
  const finished = run ? (run.finishedAt ?? run.startedAt) : null;
  const panelProps = { 'aria-expanded': panelOpen, 'aria-controls': panelOpen ? panelId : undefined } as const;

  return (
    <>
      {status.isPending && (
        <Typography variant="body2" color="text.secondary" role="status">
          Loading refresh status…
        </Typography>
      )}
      {status.isError && (
        <>
          <Typography variant="body2" color="error" role="alert">
            {problemText(status.error, 'Could not load the refresh status.')}
          </Typography>
          <Button
            size="small"
            onClick={() => {
              void status.refetch();
            }}
          >
            Retry
          </Button>
        </>
      )}
      {data && run && finished && (
        <Link
          component="button"
          type="button"
          underline="hover"
          color="text.secondary"
          onClick={(event) => {
            onOpenPanel(event.currentTarget);
          }}
          {...panelProps}
        >
          <Typography component="span" variant="body2" sx={numeric}>
            {run.status === 'failed' ? 'Last attempt' : 'Last refreshed'}{' '}
            <time dateTime={finished}>{formatAgo(finished)}</time> · {formatNumber(data.totalActive)} active
          </Typography>
        </Link>
      )}
      {data && !run && (
        <Typography variant="body2" color="text.secondary">
          No master list yet
        </Typography>
      )}
      {chip && (
        <Chip
          size="small"
          variant="outlined"
          clickable
          color={chip.color}
          icon={chip.icon}
          label={chip.label}
          onClick={(event) => {
            onOpenPanel(event.currentTarget);
          }}
          {...panelProps}
        />
      )}
      <RefreshActions busy={busy} disabled={!data} onStart={onStart} buttonRef={refreshButtonRef} />
    </>
  );
}
