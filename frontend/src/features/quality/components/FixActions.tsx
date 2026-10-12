import CloudIcon from '@mui/icons-material/CloudDownloadOutlined';
import FactCheckIcon from '@mui/icons-material/FactCheckOutlined';
import RefreshIcon from '@mui/icons-material/RefreshOutlined';
import { Alert, Button, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import { stateAlpha } from '@/theme';
import type { QualityWatch } from '../hooks/useQualityWatch';
import { startErrorText } from '../lib/errors';
import { JobProgress } from './JobProgress';

const busyStyle = { '&[aria-disabled="true"]': { opacity: stateAlpha.busy, cursor: 'default' } } as const;

/**
 * What the owner can do about one instrument: check it again, re-fetch it from Dhan (then rebuild its aggregates and
 * check it), or only rebuild its aggregates (then check it). Each starts a job; its progress shows here and the result
 * is read again when it ends. Re-fetch says why it is not available when Dhan cannot be asked.
 */
export function FixActions({ instrumentId, watch }: { instrumentId: string; watch: QualityWatch }) {
  const hintId = useId();
  const { busy, running, dhan, startJob, startError, dismissStartError } = watch;
  const refetchBlocked = dhan !== null && !dhan.canFetch;
  const mine = running?.instrumentId === instrumentId;
  const act = (action: () => void) => () => {
    if (!busy) action();
  };

  return (
    <Stack spacing={1.5}>
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        <Button
          variant="outlined"
          startIcon={<FactCheckIcon />}
          aria-disabled={busy || undefined}
          sx={busyStyle}
          onClick={act(() => {
            startJob({ kind: 'run', instrumentId });
          })}
        >
          Check now
        </Button>
        <Button
          variant="outlined"
          startIcon={<CloudIcon />}
          disabled={refetchBlocked}
          aria-disabled={busy || undefined}
          aria-describedby={refetchBlocked ? hintId : undefined}
          sx={busyStyle}
          onClick={act(() => {
            startJob({ kind: 'refetch', instrumentId });
          })}
        >
          Re-fetch
        </Button>
        <Button
          variant="outlined"
          startIcon={<RefreshIcon />}
          aria-disabled={busy || undefined}
          sx={busyStyle}
          onClick={act(() => {
            startJob({ kind: 'refresh', instrumentId });
          })}
        >
          Refresh aggregates
        </Button>
      </Stack>
      {refetchBlocked && (
        <Typography id={hintId} variant="body2" color="text.secondary">
          Re-fetch is not available: {dhan.reason ?? 'Dhan cannot be asked now.'}
        </Typography>
      )}
      <Typography variant="body2" color="text.secondary">
        Re-fetch asks Dhan for this instrument&apos;s missing days, rebuilds its aggregates and checks it again. Refresh
        aggregates rebuilds them from the 1-minute bars and checks it again.
      </Typography>
      {startError ? (
        <Alert severity="error" onClose={dismissStartError}>
          {startErrorText(startError)}
        </Alert>
      ) : null}
      {running && (
        <>
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            {mine ? 'Working on this instrument' : 'Another job is running; these buttons work again when it ends.'}
          </Typography>
          <JobProgress job={running} />
        </>
      )}
    </Stack>
  );
}
