import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import type { BrokerHistoryStatus } from '@/shared/api';
import { invalidateBrokerHistory } from '../api/brokerHistoryMutations';

/** The latest run of each kind with its state: it changes when a run starts and again when it ends. */
const signatureOf = (status: BrokerHistoryStatus): string =>
  [status.trades.lastRun, status.ledger.lastRun].map((run) => `${run?.id ?? '-'}:${run?.status ?? '-'}`).join('|');

/**
 * A trades and ledger run ended (this screen's Sync now, the nightly service, or another tab): the trades, the rebuilt lots
 * and the ledger changed, so read them again. A run seen running and then idle counts however it began, also when the page
 * was opened mid-run. A status that is idle at first sight is the starting point, not news; after that, a different latest
 * run (one that began and ended between two reads) counts too. Each end reloads once.
 */
export function useRefreshOnBrokerRun(status: BrokerHistoryStatus | undefined): void {
  const queryClient = useQueryClient();
  const running = status?.running;
  const signature = status ? signatureOf(status) : undefined;
  const wasRunning = useRef(false);
  const seen = useRef<string | undefined>(undefined);
  useEffect(() => {
    if (running === undefined || signature === undefined) return;
    if (running) {
      wasRunning.current = true;
      return;
    }
    const changed = seen.current !== undefined && seen.current !== signature;
    if (wasRunning.current || changed) void invalidateBrokerHistory(queryClient);
    wasRunning.current = false;
    seen.current = signature;
  }, [running, signature, queryClient]);
}
