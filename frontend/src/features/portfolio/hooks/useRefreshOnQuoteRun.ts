import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import type { ServiceView } from '@/shared/api';
import { invalidateHoldings } from '../api/portfolioMutations';

/**
 * A quote run ended: the prices changed, so the holdings and the totals are read again. This holds for a run started
 * here, by the schedule or from another tab: the run is seen to stop running, or a run with a new id is seen to have
 * finished. What is there when the page opens is the starting point, not news.
 */
export function useRefreshOnQuoteRun(service: ServiceView | null): void {
  const queryClient = useQueryClient();
  const seen = useRef<{ running: boolean; finishedId: string | undefined } | null>(null);
  useEffect(() => {
    if (!service) return;
    const running = service.status === 'running';
    const run = service.lastRun;
    const finishedId = run && run.status !== 'running' ? run.id : undefined;
    const before = seen.current;
    const stopped = before?.running === true && !running;
    const newRun = before?.finishedId !== undefined && finishedId !== undefined && finishedId !== before.finishedId;
    if (stopped || newRun) void invalidateHoldings(queryClient);
    seen.current = { running, finishedId };
  }, [service, queryClient]);
}
