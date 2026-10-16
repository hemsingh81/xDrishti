import { Table, TableBody } from '@mui/material';
import type { HoldingGroups } from '@/shared/api';
import type { GroupBy, SortState } from '../lib/filters';
import type { GroupSortField } from '../lib/groups';
import { groupTableMinWidth } from './groupColumns';
import type { GroupScope } from './GroupChildren';
import { GroupRow } from './GroupRow';
import { GroupTableHead } from './GroupTableHead';

interface GroupTableProps {
  data: HoldingGroups;
  by: GroupBy;
  sort: SortState;
  expanded: ReadonlySet<string>;
  scope: GroupScope;
  onSort: (field: GroupSortField) => void;
  onToggle: (key: string) => void;
}

/**
 * The groups from `md`: sticky column titles, the total row "All holdings" stuck under them, then one row per group that
 * opens to its holdings. The layout is fixed, so an opened group's holdings never widen the table.
 */
export function GroupTable({ data, by, sort, expanded, scope, onSort, onToggle }: GroupTableProps) {
  return (
    <Table
      stickyHeader
      size="small"
      aria-label={`Holdings by ${by}`}
      sx={{
        tableLayout: 'fixed',
        minWidth: { xs: groupTableMinWidth.xs, md: groupTableMinWidth.md, lg: groupTableMinWidth.lg },
      }}
    >
      <GroupTableHead by={by} total={data.total} sort={sort} onSort={onSort} />
      <TableBody>
        {data.groups.map((group) => (
          <GroupRow
            key={group.key}
            group={group}
            expanded={expanded.has(group.key)}
            scope={scope}
            onToggle={() => {
              onToggle(group.key);
            }}
          />
        ))}
      </TableBody>
    </Table>
  );
}
