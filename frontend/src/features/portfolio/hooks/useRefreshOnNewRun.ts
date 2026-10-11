import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { invalidateHoldings } from '../api/portfolioMutations';

/**
 * A run finished that this screen did not start (an automatic refresh, or one from another tab): the holdings changed,
 * so reload them. The first run seen is the starting point, not news.
 */
export function useRefreshOnNewRun(lastRunId: string | undefined): void {
  const queryClient = useQueryClient();
  const seen = useRef<string | undefined>(undefined);
  useEffect(() => {
    if (lastRunId === undefined) return;
    if (seen.current !== undefined && seen.current !== lastRunId) void invalidateHoldings(queryClient);
    seen.current = lastRunId;
  }, [lastRunId, queryClient]);
}
