import LockIcon from '@mui/icons-material/LockOutlined';
import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { formatIstDate, formatIstDateTime, formatIstTime } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { settingsQuery, useChangeSettings } from '../api/switchApi';
import { PasswordReasonDialog } from './PasswordReasonDialog';
import { RetryError } from './RetryError';

const word = (on: boolean) => (on ? 'ON' : 'OFF');

/** The owner's execution switch: in force, the waiting switch-on, the last rows, Turn on / Turn off with a fresh password. */
export function ExecutionSwitchCard() {
  const { data, error, isPending, refetch } = useQuery(settingsQuery);
  const change = useChangeSettings();
  // The act is fixed when the dialog opens; a refresh behind it cannot change what the password is given for.
  const [target, setTarget] = useState<boolean | null>(null);
  const [open, setOpen] = useState(false);
  const close = () => {
    change.reset();
    setOpen(false);
  };
  const ask = (turnOn: boolean) => {
    change.reset();
    setTarget(turnOn);
    setOpen(true);
  };
  const on = data?.inForce?.executionEnabled === true;
  const pending = data?.pending ?? null;
  return (
    <section aria-label="Execution switch">
      <Typography variant="h2" sx={{ fontSize: 18, mb: 1 }}>
        Execution switch
      </Typography>
      {isPending && <LoadingState label="Loading the execution switch" />}
      {error && <RetryError error={error} onRetry={() => void refetch()} />}
      {data && (
        <Stack sx={{ gap: 1 }}>
          <Typography>
            Execution is <strong>{word(on)}</strong>
            {data.inForce === null && ' (never switched; counts as off)'}.
          </Typography>
          {pending && (
            <Alert severity="info" role="status">
              A switch-on is waiting: it takes effect at {formatIstTime(pending.effectiveFrom)} IST on{' '}
              {formatIstDate(pending.effectiveFrom)} after a full closed session.
            </Alert>
          )}
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            Switching on needs your password and waits for one full closed session; switching off needs your password
            too and is in force at once (it also cancels a waiting switch-on).
          </Typography>
          <Stack direction="row" sx={{ gap: 1 }}>
            <Button
              variant="contained"
              disabled={on || pending !== null}
              onClick={() => {
                ask(true);
              }}
            >
              Turn on
            </Button>
            <Button
              variant="outlined"
              color="error"
              disabled={!on && pending === null}
              onClick={() => {
                ask(false);
              }}
            >
              Turn off
            </Button>
          </Stack>
          <Stack direction="row" sx={{ alignItems: 'center', gap: 0.5 }}>
            <LockIcon fontSize="small" aria-hidden />
            <Typography variant="body2">
              Auto-live: {word(data.inForce?.autoLiveEnabled === true)}, locked. No control changes it in this build.
            </Typography>
          </Stack>
          {data.history.length > 0 && (
            <Box component="ul" aria-label="Recent switch changes" sx={{ m: 0, pl: 3 }}>
              {data.history.slice(0, 5).map((r) => (
                <li key={r.id}>
                  {formatIstDateTime(r.changedAt)}: {word(r.executionEnabled)} by {r.actor}, from{' '}
                  {formatIstDateTime(r.effectiveFrom)}: {r.reason}
                </li>
              ))}
            </Box>
          )}
        </Stack>
      )}
      <PasswordReasonDialog
        open={open}
        title={target ? 'Turn execution on' : 'Turn execution off'}
        confirmLabel={target ? 'Turn on' : 'Turn off'}
        intro={
          <Typography variant="body2" sx={{ mb: 1 }}>
            {target
              ? 'Execution switches on at 09:00 IST of the next trading day after a full closed session.'
              : 'Execution switches off at once and any waiting switch-on is cancelled.'}
          </Typography>
        }
        pending={change.isPending}
        error={change.error}
        onClose={close}
        onSubmit={({ password, reason }) => {
          change.mutate(
            { executionEnabled: target === true, autoLiveEnabled: false, reason, password },
            { onSuccess: close },
          );
        }}
      />
    </section>
  );
}
