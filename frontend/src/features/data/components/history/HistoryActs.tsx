import { Alert, Box, Button, Typography } from '@mui/material';
import { Suspense, useEffect, useRef, useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { PasswordReasonDialog } from '@/features/execution';
import { formatNumber } from '@/shared/lib/format';
import { useProbeSeam, useStartBackfill, type HistoryOverview } from '../../api/historyQueries';
import { actErrorText, withActText } from '../../lib/history';

type Act = 'backfill' | 'probe';

const titles: Readonly<Record<Act, string>> = { backfill: 'Start backfill', probe: 'Probe seam' };

/** The two owner acts, each asking for a fresh password. While the switch is OFF both are disabled and the reason is their description. */
export function HistoryActs({ data }: { data: HistoryOverview }) {
  const [open, setOpen] = useState<Act | null>(null);
  const [notice, setNotice] = useState<React.ReactNode>(null);
  const [isError, setIsError] = useState(false);
  const backfill = useStartBackfill();
  const probe = useProbeSeam();
  const on = data.switch.enabled;
  const reasonId = 'history-switch-reason';
  const mutation = open === 'probe' ? probe : backfill;

  const openRef = useRef<Act | null>(null);
  useEffect(() => {
    openRef.current = open;
  }, [open]);
  // A request in flight keeps going when the dialog is closed: it is not reset, so its outcome still arrives as the notice.
  const close = () => {
    setOpen(null);
    if (!backfill.isPending) backfill.reset();
    if (!probe.isPending) probe.reset();
  };
  const failed = (error: unknown) => {
    if (openRef.current === null) {
      setNotice(actErrorText(error));
      setIsError(true);
    }
  };
  const submit = ({ password }: { password: string }) => {
    if (open === 'probe') {
      probe.mutate(password, {
        onSuccess: (r) => {
          setNotice(
            <>
              Probed {formatNumber(r.probed)} instruments: {formatNumber(r.passed)} passed, {formatNumber(r.unresolved)}{' '}
              unresolved, {formatNumber(r.noOverlap)} without overlap, {formatNumber(r.failed)} failed (
              {formatNumber(r.requests)} requests).
              <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
                {r.lines.map((line, i) => (
                  <li key={`${String(i)}-${line}`}>{line}</li>
                ))}
              </Box>
            </>,
          );
          close();
        },
        onError: failed,
      });
    } else {
      backfill.mutate(password, {
        onSuccess: (r) => {
          setNotice(
            r.started ? (
              <>
                Backfill started: {formatNumber(r.chunksPending)} chunks pending. Follow the run on the{' '}
                <RouterLink to="/services">Services screen</RouterLink>.
              </>
            ) : (
              'Nothing is pending, so no run was started.'
            ),
          );
          close();
        },
        onError: failed,
      });
    }
  };

  return (
    <Box component="section" aria-label="Owner acts" sx={{ display: 'grid', gap: 1 }}>
      <Typography variant="body2" color="text.secondary" id={reasonId}>
        {on
          ? 'Each act asks for your password again.'
          : (data.switch.reason ?? 'The switch is off, so both acts are refused.')}
      </Typography>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
        {(['backfill', 'probe'] as const).map((act) => (
          <Button
            key={act}
            variant="outlined"
            disabled={!on}
            aria-describedby={reasonId}
            onClick={() => {
              setNotice(null);
              setIsError(false);
              setOpen(act);
            }}
          >
            {titles[act]}
          </Button>
        ))}
      </Box>
      {notice !== null && (
        <Alert
          severity={isError ? 'error' : 'success'}
          onClose={() => {
            setNotice(null);
          }}
        >
          {notice}
        </Alert>
      )}
      {open !== null && (
        <Suspense fallback={null}>
          <PasswordReasonDialog
            open
            askReason={false}
            title={titles[open]}
            confirmLabel={titles[open]}
            pending={mutation.isPending}
            error={mutation.error ? withActText(mutation.error) : null}
            intro={
              <Typography variant="body2">
                {open === 'backfill'
                  ? 'This starts fetching 1-minute bars from Dhan. Your password is asked again for this act.'
                  : 'This asks Dhan for a few bars to compare them with the CSV. It changes no bars. Your password is asked again.'}
              </Typography>
            }
            onSubmit={submit}
            onClose={close}
          />
        </Suspense>
      )}
    </Box>
  );
}
