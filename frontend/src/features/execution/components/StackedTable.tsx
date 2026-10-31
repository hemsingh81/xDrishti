import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import type { ReactNode } from 'react';

export interface Col<Row> {
  id: string;
  header: string;
  render: (row: Row) => ReactNode;
  align?: 'right';
}

interface Props<Row> {
  label: string;
  columns: readonly Col<Row>[];
  rows: readonly Row[];
  rowKey: (row: Row) => string;
}

/**
 * A table that becomes a stack of cards under 600 px (spec P12-10 criterion 14): each cell then carries its header as a
 * visible label, so nothing needs sideways scrolling on a phone.
 */
export function StackedTable<Row>({ label, columns, rows, rowKey }: Props<Row>) {
  return (
    <TableContainer>
      <Table
        size="small"
        aria-label={label}
        className="xd-stacked"
        sx={(theme) => ({
          [theme.breakpoints.down('sm')]: {
            '& thead': { position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' },
            '& tbody tr': {
              display: 'block',
              border: 1,
              borderColor: 'divider',
              borderRadius: 1,
              mb: 1.5,
              p: 1,
            },
            '& tbody td, & tbody th': {
              display: 'flex',
              justifyContent: 'space-between',
              gap: 2,
              border: 0,
              py: 0.5,
              textAlign: 'right',
            },
          },
        })}
      >
        <TableHead>
          <TableRow>
            {columns.map((c) => (
              <TableCell key={c.id} align={c.align ?? 'left'}>
                {c.header}
              </TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={rowKey(row)}>
              {columns.map((c, index) => (
                <TableCell
                  key={c.id}
                  align={c.align ?? 'left'}
                  {...(index === 0 ? { component: 'th', scope: 'row' } : {})}
                >
                  <Box
                    component="span"
                    sx={(theme) => ({
                      display: 'none',
                      color: 'text.secondary',
                      [theme.breakpoints.down('sm')]: { display: 'inline' },
                    })}
                  >
                    {c.header}
                  </Box>
                  <Box component="span">{c.render(row)}</Box>
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
