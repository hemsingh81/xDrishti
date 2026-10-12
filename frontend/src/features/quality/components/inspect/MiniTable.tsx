import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import { alpha } from '@mui/material/styles';
import type { ReactNode } from 'react';

export interface MiniColumn {
  header: string;
  numeric?: boolean;
}

export interface MiniRow {
  key: string;
  cells: ReactNode[];
  /** The row is wrong (a difference): tinted, on top of the words in its cells. */
  flagged?: boolean;
}

interface MiniTableProps {
  label: string;
  columns: readonly MiniColumn[];
  rows: readonly MiniRow[];
}

/** A small table for the evidence of a check: scrolls sideways when the drawer is narrow. */
export function MiniTable({ label, columns, rows }: MiniTableProps) {
  return (
    <TableContainer sx={{ maxHeight: 360 }}>
      <Table size="small" stickyHeader aria-label={label}>
        <TableHead>
          <TableRow>
            {columns.map((column) => (
              <TableCell key={column.header} align={column.numeric ? 'right' : 'left'}>
                {column.header}
              </TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((row) => (
            <TableRow
              key={row.key}
              sx={row.flagged ? { bgcolor: (theme) => alpha(theme.palette.error.main, 0.08) } : undefined}
            >
              {row.cells.map((cell, index) => (
                <TableCell key={columns[index]?.header ?? index} align={columns[index]?.numeric ? 'right' : 'left'}>
                  <Box component="span">{cell}</Box>
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
