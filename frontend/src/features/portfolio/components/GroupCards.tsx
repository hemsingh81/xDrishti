import { Stack } from '@mui/material';
import type { HoldingGroups } from '@/shared/api';
import type { GroupBy } from '../lib/filters';
import type { GroupScope } from './GroupChildren';
import { GroupCard, TotalCard } from './GroupCard';
import { isUnallocatedGroup } from '../lib/income';
import { UnallocatedCard } from './UnallocatedRow';

interface GroupCardsProps {
  data: HoldingGroups;
  by: GroupBy;
  expanded: ReadonlySet<string>;
  scope: GroupScope;
  onToggle: (key: string) => void;
}

/** The groups on a phone: the total, then one card per group. */
export function GroupCards({ data, by, expanded, scope, onToggle }: GroupCardsProps) {
  return (
    <Stack component="ul" aria-label={`Holdings by ${by}`} spacing={1.5} sx={{ p: 1.5, m: 0 }}>
      <TotalCard total={data.total} />
      {data.groups.map((group) =>
        isUnallocatedGroup(group) ? (
          <UnallocatedCard key={group.key} group={group} />
        ) : (
          <GroupCard
            key={group.key}
            group={group}
            expanded={expanded.has(group.key)}
            scope={scope}
            onToggle={() => {
              onToggle(group.key);
            }}
          />
        ),
      )}
    </Stack>
  );
}
