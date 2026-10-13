import { useQuery } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { groupsQuery } from '../api/groupQueries';
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
 * stack dimension for the bar chart comes with it). Re-read every minute while the market is open, like the flat list.
 */
export function GroupedHoldings({ by, scope, emptyState, onChange }: GroupedHoldingsProps) {
  const query = useQuery({ ...groupsQuery(scope.filters, by), refetchInterval: scope.pollMs });
  return (
    <>
      <AllocationCard by={by} query={query} />
      <GroupGrid key={by} query={query} by={by} scope={scope} emptyState={emptyState} onChange={onChange} />
    </>
  );
}
