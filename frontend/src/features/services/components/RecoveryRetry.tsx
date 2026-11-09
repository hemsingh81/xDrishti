import { Alert, Button } from '@mui/material';
import { useState } from 'react';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { useToast } from '@/shared/ui/toastContext';
import { useRetryRecovery } from '../api/recoveryApi';
import { problemText } from '../lib/errors';

/** One Retry for the whole pass (it never retries a single row), asked to confirm. The 409 is shown in the server's words. */
export function RecoveryRetry({ disabled }: { disabled: boolean }) {
  const [asking, setAsking] = useState(false);
  const [refused, setRefused] = useState<string | null>(null);
  const retry = useRetryRecovery();
  const toast = useToast();

  const confirm = () => {
    retry.mutate(undefined, {
      onSuccess: () => {
        setRefused(null);
        toast.notify('Recovery started.', 'success');
      },
      onError: (error) => {
        setRefused(problemText(error, 'Recovery could not be started.'));
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
        disabled={disabled}
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
        This runs the whole recovery pass again, for every service that can be retried. It places no orders.
      </ConfirmDialog>
    </>
  );
}
