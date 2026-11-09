import { Alert, Button } from '@mui/material';
import { useState } from 'react';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { useToast } from '@/shared/ui/toastContext';
import { useRetryRecovery } from '../api/recoveryApi';
import { codeOf, problemText } from '../lib/errors';

/** One Retry for the whole pass (it never retries a single row), asked to confirm. The 409 is shown in the server's words. */
export function RecoveryRetry({ disabled }: { disabled: boolean }) {
  const [asking, setAsking] = useState(false);
  const [refused, setRefused] = useState<string | null>(null);
  const [blocked, setBlocked] = useState(false);
  const retry = useRetryRecovery();
  const toast = useToast();

  const confirm = () => {
    retry.mutate(undefined, {
      onSuccess: (accepted) => {
        setRefused(null);
        setBlocked(false);
        toast.notify(
          accepted.started ? 'Recovery started.' : 'Nothing to run; re-checked what was partial.',
          accepted.started ? 'success' : 'info',
        );
      },
      onError: (error) => {
        setRefused(problemText(error, 'Recovery could not be started.'));
        // These refusals do not change by pressing again; the cause has to be fixed first.
        setBlocked(['recovery.not_recorded', 'recovery.plan_unavailable'].includes(codeOf(error) ?? ''));
      },
      onSettled: () => {
        setAsking(false);
      },
    });
  };

  return (
    <>
      <Button
        variant="contained"
        disabled={disabled || blocked}
        onClick={() => {
          setAsking(true);
        }}
      >
        Retry recovery
      </Button>
      {refused && (
        <Alert severity="warning" sx={{ mt: 1 }}>
          {refused}
        </Alert>
      )}
      <ConfirmDialog
        open={asking}
        title="Retry recovery?"
        confirmLabel="Start recovery"
        busy={retry.isPending}
        onConfirm={confirm}
        onCancel={() => {
          setAsking(false);
        }}
      >
        This runs the whole recovery pass again. Where a row only needs its stored data checked again (partly
        recovered), that is all it does. It places no orders.
      </ConfirmDialog>
    </>
  );
}
