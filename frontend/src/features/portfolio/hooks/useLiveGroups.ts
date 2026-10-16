import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import type { HoldingGroups } from '@/shared/api';
import { liveMembersQuery } from '../api/liveQueries';
import type { GroupBy, PortfolioFilters } from '../lib/filters';
import { isUnallocatedKey } from '../lib/groups';
import { liveGroups, membershipOf } from '../lib/liveTotals';
import { useLive, useLiveBase } from './liveContext';
import { liveReadMs } from './useLiveFeed';
import { useRereadOnMismatch } from './useRereadOnMismatch';

/** More groups than this are not asked for their members one by one: such a grouping stays as read. */
const maxMemberGroups = 60;

/**
 * The groups at the pushed prices (undefined: as read). Accounts and buckets are told from the rows; sectors and baskets
 * need the members of each group, asked for once the groups are known (and again with every read). A search text or the
 * closed status leaves the groups as read: the holdings behind the totals are not the scope's. When the groups and the
 * holdings do not fit (read at different moments), the oldest of the reads is read again (rate-limited).
 */
export function useLiveGroups(groups: UseQueryResult<HoldingGroups>, by: GroupBy, filters: PortfolioFilters) {
  const { following, scope } = useLive();
  const { rows, read } = useLiveBase();
  const data = groups.data;
  const eligible =
    following && data !== undefined && rows !== undefined && filters.q.trim() === '' && filters.status !== 'closed';
  const byRows = by === 'account' || by === 'bucket';
  // The Unallocated row has no holdings: it is not asked for its members (and the recompute leaves it as read).
  const keys = data?.groups.map((group) => group.key).filter((key) => !isUnallocatedKey(key)) ?? [];
  const members = useQuery({
    ...liveMembersQuery({ account: scope.account, bucket: scope.bucket, by, keys }),
    enabled: eligible && !byRows && keys.length > 0 && keys.length <= maxMemberGroups,
    refetchInterval: liveReadMs,
  });
  const lists = members.data;

  const live = useMemo(() => {
    if (!eligible) return undefined;
    const membership = membershipOf(by, lists);
    return membership ? liveGroups(data, rows, membership, filters.status === 'open') : undefined;
  }, [eligible, by, lists, data, rows, filters.status]);

  const { dataUpdatedAt, refetch } = groups;
  const rereadGroups = useCallback(() => {
    void refetch({ cancelRefetch: false });
  }, [refetch]);
  const { dataUpdatedAt: membersUpdatedAt, refetch: refetchMembers } = members;
  const rereadMembers = useCallback(() => {
    void refetchMembers({ cancelRefetch: false });
  }, [refetchMembers]);
  // Everything needed is read (the members too, for sectors and baskets) and still it does not fit: the oldest read is read again.
  const ready = eligible && (byRows || lists !== undefined);
  useRereadOnMismatch(ready && live === undefined, [
    read,
    { updatedAt: dataUpdatedAt, reread: rereadGroups },
    ...(byRows ? [] : [{ updatedAt: membersUpdatedAt, reread: rereadMembers }]),
  ]);
  return live;
}
