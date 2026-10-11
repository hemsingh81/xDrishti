import { Skeleton, TableCell, TableRow } from '@mui/material';
import { cellSx } from './cellStyle';
import type { GridColumn } from './types';

interface GridSkeletonRowsProps<Row> {
  columns: readonly GridColumn<Row>[];
  count: number;
  selectable: boolean;
}

/** Placeholder rows for the first load; they keep the column layout so nothing jumps when the data arrives. */
export function GridSkeletonRows<Row>({ columns, count, selectable }: GridSkeletonRowsProps<Row>) {
  return Array.from({ length: count }, (_, row) => (
    <TableRow key={row} role="row" aria-hidden>
      {selectable && <TableCell padding="checkbox" />}
      {columns.map((column, col) => (
        <TableCell key={column.id} sx={cellSx(column, { sticky: false, selectable, head: false })}>
          <Skeleton variant="text" width={(col + row) % 3 === 0 ? 96 : 56} />
        </TableCell>
      ))}
    </TableRow>
  ));
}
