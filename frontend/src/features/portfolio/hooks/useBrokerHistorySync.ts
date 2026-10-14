import { useQuery } from '@tanstack/react-query';
import { useSyncBrokerHistory } from '../api/brokerHistoryMutations';
import { brokerHistoryStatusQuery } from '../api/brokerHistoryQueries';
import { useRefreshOnBrokerRun } from './useRefreshOnBrokerRun';

/**
 * "Sync now" for one account's trades and ledger: the status (polled while a run runs), the call that starts a run, and the
 * reload of the holdings, trades and ledger when a run ends. The run starts at once on the API's side; the status shows it.
 */
export function useBrokerHistorySync(accountId: string) {
  const status = useQuery(brokerHistoryStatusQuery(accountId));
  useRefreshOnBrokerRun(status.data);
  const sync = useSyncBrokerHistory(accountId);
  return {
    status,
    start: () => {
      sync.mutate();
    },
    /** A run is running, or the request that starts one is on its way. */
    running: sync.isPending || (status.data?.running ?? false),
    error: sync.error,
  };
}
