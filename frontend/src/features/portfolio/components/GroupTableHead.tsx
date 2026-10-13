import { Stack, TableBody, TableCell, TableHead, TableRow, TableSortLabel, Typography } from '@mui/material';
import { table } from '@/theme';
import { formatNumber } from '@/shared/lib/format';
import type { GroupBy, SortState } from '../lib/filters';
import { groupNoun, type GroupSortField } from '../lib/groups';
import { figureCellSx, groupColumns, type GroupColumn, type GroupSortKey } from './groupColumns';
import type { GroupFigures } from './GroupFigures';

/** Height of the header row: the total row sticks right under it. */
const headHeight = table.rowHeight + 1;

interface SortButtonsProps {
  keys: readonly GroupSortKey[];
  sort: SortState;
  onSort: (field: GroupSortField) => void;
}

function SortButtons({ keys, sort, onSort }: SortButtonsProps) {
  return (
    <Stack direction="row" sx={{ gap: 1, justifyContent: 'inherit', alignItems: 'center' }}>
      {keys.map((key) => {
        const active = sort.field === key.field;
        return (
          <TableSortLabel
            key={key.field}
            active={active}
            direction={active && sort.descending ? 'desc' : 'asc'}
            aria-label={`Sort by ${key.label}`}
            onClick={() => {
              onSort(key.field);
            }}
          >
            {key.text ?? key.label}
          </TableSortLabel>
        );
      })}
    </Stack>
  );
}

/** `aria-sort` of a header cell: the direction when one of its fields orders the groups. */
function sortDirection(keys: readonly GroupSortKey[] | undefined, sort: SortState): 'asc' | 'desc' | false {
  if (!keys?.some((k) => k.field === sort.field)) return false;
  return sort.descending ? 'desc' : 'asc';
}

interface GroupTableHeadProps {
  by: GroupBy;
  total: GroupFigures;
  sort: SortState;
  onSort: (field: GroupSortField) => void;
}

/**
 * The column titles (sortable ones are buttons) and, stuck right under them, the total row "All holdings" (a body row of
 * its own: its figures are cells, not column titles).
 */
export function GroupTableHead({ by, total, sort, onSort }: GroupTableHeadProps) {
  const nameKeys: readonly GroupSortKey[] = [{ field: 'name', label: groupNoun[by] }];
  const stuck = { position: 'sticky', top: headHeight, zIndex: 2, bgcolor: 'background.paper' } as const;
  return (
    <>
      <TableHead>
        <TableRow>
          <TableCell scope="col" sortDirection={sortDirection(nameKeys, sort)} sx={{ paddingInline: 1.25 }}>
            <SortButtons keys={nameKeys} sort={sort} onSort={onSort} />
          </TableCell>
          {groupColumns.map((column: GroupColumn) => (
            <TableCell
              key={column.id}
              scope="col"
              sortDirection={sortDirection(column.sorts, sort)}
              sx={figureCellSx(column)}
            >
              {column.sorts ? <SortButtons keys={column.sorts} sort={sort} onSort={onSort} /> : column.header}
            </TableCell>
          ))}
        </TableRow>
      </TableHead>
      <TableBody>
        <TableRow>
          <TableCell component="th" scope="row" variant="body" sx={{ ...stuck, paddingInline: 1.25, fontWeight: 600 }}>
            <Typography component="span" sx={{ fontWeight: 600 }}>
              All holdings
            </Typography>
            <Typography component="span" variant="caption" color="text.secondary" sx={{ display: 'block' }}>
              {`${formatNumber(total.holdings)} in this view`}
            </Typography>
          </TableCell>
          {groupColumns.map((column) => (
            <TableCell key={column.id} variant="body" sx={{ ...figureCellSx(column), ...stuck, fontWeight: 600 }}>
              {column.render(total)}
            </TableCell>
          ))}
        </TableRow>
      </TableBody>
    </>
  );
}
