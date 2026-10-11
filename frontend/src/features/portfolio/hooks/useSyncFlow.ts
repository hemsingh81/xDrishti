import { useState } from 'react';
import { useToast } from '@/shared/ui/toastContext';
import { useSyncNow } from '../api/portfolioMutations';
import { confirmationOf, type Confirmation } from '../lib/errors';
import { summariseRun } from '../lib/syncText';

/**
 * "Sync now" for one account: start it, tell the owner how it went, and handle the safeguard. When Dhan's answer would
 * close all or most holdings the API refuses until the owner confirms; the numbers they saw are sent back with the
 * confirmation, and if the answer has changed since (`confirmation_stale`) the dialog reopens with the fresh numbers
 * and nothing is retried by itself.
 */
export function useSyncFlow(accountId: string | null) {
  const sync = useSyncNow(accountId ?? '');
  const { notify } = useToast();
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null);

  function start(confirmed?: Confirmation) {
    sync.mutate(
      { confirmed },
      {
        onSuccess: (run) => {
          setConfirmation(null);
          const summary = summariseRun(run);
          notify(`${summary.title}: ${summary.detail}.`, run.status === 'succeeded' ? 'success' : 'error');
        },
        onError: (error) => {
          setConfirmation(confirmationOf(error));
        },
      },
    );
  }

  return {
    run: () => {
      start();
    },
    /** Confirms the numbers the dialog shows. */
    confirm: () => {
      if (confirmation) start(confirmation);
    },
    cancelConfirmation: () => {
      setConfirmation(null);
      sync.reset();
    },
    pending: sync.isPending,
    /** The refusal to show; the safeguard has its own dialog. */
    error: confirmation ? null : sync.error,
    confirmation,
  };
}
