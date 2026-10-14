import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import type { HoldingGroups } from '@/shared/api';
import { groupsQuery } from '../api/groupQueries';
import { useLiveGroups } from '../hooks/useLiveGroups';
import type { GroupBy, PortfolioFilters } from '../lib/filters';
import { AllocationCard } from './AllocationCard';
import type { GroupScope } from './GroupChildren';
import { GroupGrid } from './GroupGrid';

interface GroupedHoldingsProps {
  by: GroupBy;
  scope: GroupScope;
  emptyState: ReactNode;
  onChange: (patch: Partial<PortfolioFilters>) => void;
}

/**
 * The Group by view of the Portfolio screen: the Charts card and the group grid, both fed by one groups request (the
 * stack dimension for the bar chart comes with it). Re-read every minute while the market is open, like the flat list (every 5
 * minutes while the live hub follows the screen, whose pushed prices move the group rows in between).
 */
export function GroupedHoldings({ by, scope, emptyState, onChange }: GroupedHoldingsProps) {
  const query = useQuery({ ...groupsQuery(scope.filters, by), refetchInterval: scope.pollMs });
  // The rows move with the prices the hub pushed; the charts are drawn from the read (they would redraw every second).
  const live = useLiveGroups(query, by, scope.filters);
  const shown = live === undefined ? query : ({ ...query, data: live } as UseQueryResult<HoldingGroups>);
  return (
    <>
      <AllocationCard by={by} query={query} />
      <GroupGrid key={by} query={shown} by={by} scope={scope} emptyState={emptyState} onChange={onChange} />
    </>
  );
}
