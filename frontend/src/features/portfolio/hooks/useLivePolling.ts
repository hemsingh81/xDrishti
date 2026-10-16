import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { liveBaseQuery } from '../api/liveQueries';
import { liveReadMs, useLiveFeed } from './useLiveFeed';

/**
 * The live hub of the Portfolio screen, and how often the page reads its figures because of it: while the hub follows the
 * screen the list is read every 5 minutes (pushes move it in between), the totals and groups too once the holdings they are
 * worked out from are read; without the hub (or until then) every minute while the market is open, as before the hub.
 */
export function useLivePolling(
  market: { phase: 'closed' | 'preopen' | 'open'; pollMs: number | false },
  scope: { account: string | null; bucket: string | null },
  accountIds: readonly string[],
) {
  const live = useLiveFeed({ wanted: market.phase !== 'closed' });
  const key = accountIds.join(',');
  const liveScope = useMemo(
    () => ({ account: scope.account, bucket: scope.bucket, accounts: key === '' ? [] : key.split(',') }),
    [scope.account, scope.bucket, key],
  );
  // The totals and the groups are worked out again from the holdings of the scope; until those are read they stay as read.
  const base = useQuery({ ...liveBaseQuery(liveScope), enabled: live.following, refetchInterval: liveReadMs });
  return {
    live,
    liveScope,
    listPollMs: live.following ? liveReadMs : market.pollMs,
    totalsPollMs: live.following && base.isSuccess ? liveReadMs : market.pollMs,
  };
}
