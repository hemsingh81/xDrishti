import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import type { ServiceView } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { invalidateQuoteStatus, useRunQuotes } from '../api/quotesMutations';
import { refreshOutcome, refreshRefusal } from '../lib/refresh';

/**
 * Refresh prices: run the quote service now, show it busy while it runs (the services list is read every couple of
 * seconds meanwhile), and when the run ends say how it went (`useRefreshOnQuoteRun` reads the figures again).
 * A 409 "already running" is explained, and the run that is going on is followed the same way.
 */
export function useRefreshPrices(service: ServiceView | null) {
  const run = useRunQuotes();
  const queryClient = useQueryClient();
  const { notify } = useToast();
  // The run being followed: its id, or null for one that was already going when the click came.
  const following = useRef<{ runId: string | null; sawRunning: boolean } | null>(null);

  useEffect(() => {
    const target = following.current;
    if (!target || !service) return;
    if (service.status === 'running') {
      target.sawRunning = true;
      return;
    }
    const ended = target.runId === null ? target.sawRunning : service.lastRun?.id === target.runId;
    if (!ended) return;
    following.current = null;
    void invalidateQuoteStatus(queryClient);
    const outcome = refreshOutcome(service.lastRun);
    notify(outcome.text, outcome.tone);
  }, [service, queryClient, notify]);

  return {
    start: () => {
      run.mutate(undefined, {
        onSuccess: (started) => {
          following.current = { runId: started.id, sawRunning: true };
        },
        onError: (error) => {
          const refusal = refreshRefusal(error);
          if (refusal.following) following.current = { runId: null, sawRunning: false };
          notify(refusal.text, refusal.tone);
        },
      });
    },
    /** The click is on its way, or the service is running (started here, by the schedule or from another tab). */
    busy: run.isPending || service?.status === 'running' || service?.lastRun?.status === 'running',
    pending: run.isPending,
  };
}
