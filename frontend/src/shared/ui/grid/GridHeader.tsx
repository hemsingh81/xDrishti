import { Checkbox, TableCell, TableHead, TableRow, TableSortLabel } from '@mui/material';
import { cellSx, selectionCellSx } from './cellStyle';
import type { GridColumn, GridSort } from './types';

interface GridHeaderProps<Row> {
  columns: readonly GridColumn<Row>[];
  sort: GridSort | null | undefined;
  onSortChange: ((sort: GridSort) => void) | undefined;
  stickyFirstColumn: boolean;
  selection: {
    allOnPage: boolean;
    someOnPage: boolean;
    disabled: boolean;
    onChange: (checked: boolean) => void;
  } | null;
}

/** Sorting by the sorted column flips the direction; a new column starts ascending. */
function nextSort(current: GridSort | null | undefined, columnId: string): GridSort {
  return current?.columnId === columnId
    ? { columnId, descending: !current.descending }
    : { columnId, descending: false };
}

/** The column titles: sortable ones are buttons and announce their direction through `aria-sort`. */
export function GridHeader<Row>({ columns, sort, onSortChange, stickyFirstColumn, selection }: GridHeaderProps<Row>) {
  return (
    <TableHead>
      <TableRow role="row" aria-rowindex={1}>
        {selection && (
          <TableCell
            role="columnheader"
            aria-colindex={1}
            padding="checkbox"
            sx={selectionCellSx(stickyFirstColumn, true)}
          >
            <Checkbox
              size="small"
              checked={selection.allOnPage}
              indeterminate={selection.someOnPage}
              disabled={selection.disabled}
              onChange={(event) => {
                selection.onChange(event.target.checked);
              }}
              slotProps={{ input: { 'aria-label': 'Select all rows on this page' } }}
            />
          </TableCell>
        )}
        {columns.map((column, index) => {
          const active = sort?.columnId === column.id;
          const direction = active && sort.descending ? 'desc' : 'asc';
          const sortable = column.sortable === true && onSortChange !== undefined;
          return (
            <TableCell
              key={column.id}
              role="columnheader"
              aria-colindex={index + (selection ? 2 : 1)}
              scope="col"
              sortDirection={sortable && active ? direction : false}
              sx={cellSx(column, {
                sticky: stickyFirstColumn && index === 0,
                selectable: selection !== null,
                head: true,
              })}
            >
              {sortable ? (
                <TableSortLabel
                  active={active}
                  direction={direction}
                  onClick={() => {
                    onSortChange(nextSort(sort, column.id));
                  }}
                >
                  {column.header}
                </TableSortLabel>
              ) : (
                column.header
              )}
            </TableCell>
          );
        })}
      </TableRow>
    </TableHead>
  );
}
