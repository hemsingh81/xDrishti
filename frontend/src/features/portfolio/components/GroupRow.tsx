import { TableCell, TableRow } from '@mui/material';
import { useId } from 'react';
import type { HoldingGroup } from '@/shared/api';
import { figureCellSx, groupColumns } from './groupColumns';
import { GroupChildren, type GroupScope } from './GroupChildren';
import { GroupExpander } from './GroupExpander';

interface GroupRowProps {
  group: HoldingGroup;
  expanded: boolean;
  scope: GroupScope;
  onToggle: () => void;
}

/** One group: its name (the button that opens it) and figures, and below it, when opened, its holdings. */
export function GroupRow({ group, expanded, scope, onToggle }: GroupRowProps) {
  const regionId = useId();
  return (
    <>
      <TableRow hover>
        <TableCell component="th" scope="row" sx={{ paddingInline: 1, verticalAlign: 'top' }}>
          <GroupExpander name={group.name} expanded={expanded} controls={regionId} onToggle={onToggle} />
        </TableCell>
        {groupColumns.map((column) => (
          <TableCell key={column.id} sx={figureCellSx(column)}>
            {column.render(group)}
          </TableCell>
        ))}
      </TableRow>
      {expanded && (
        <TableRow>
          <TableCell colSpan={groupColumns.length + 1} sx={{ p: 0, border: 0 }}>
            <GroupChildren group={group} scope={scope} id={regionId} />
          </TableCell>
        </TableRow>
      )}
    </>
  );
}
