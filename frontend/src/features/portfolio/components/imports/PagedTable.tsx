import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { importPageSize } from '../../lib/dividendImport';
import { PageNav } from '../dividends/PageNav';
import { usePaged } from '../dividends/usePaged';

export interface PagedColumn<T> {
  header: string;
  /** Numbers and amounts are right-aligned in a tabular font. */
  numeric?: boolean;
  render: (row: T) => ReactNode;
}

interface PagedTableProps<T> {
  /** Names the table and its scroll region. */
  label: string;
  /** What the rows are called in the status line: "lines", "securities". */
  noun: string;
  rows: readonly T[];
  /** All the rows there are, when the API listed only some. */
  total?: number;
  columns: readonly PagedColumn<T>[];
  rowKey: (row: T, index: number) => string;
  minWidth?: number;
}

/**
 * Rows shown 50 at a time (a report may hold hundreds; only the page on screen is in the page). The table scrolls sideways
 * inside its own region on a phone, so a long name cannot widen the screen.
 */
export function PagedTable<T>({ label, noun, rows, total, columns, rowKey, minWidth = 560 }: PagedTableProps<T>) {
  const { page, setPage, shown } = usePaged(rows);
  const listed = total !== undefined && total > rows.length;
  return (
    <>
      <TableContainer role="region" aria-label={`${label}, scrolls sideways`} tabIndex={0}>
        <Table size="small" aria-label={label} sx={{ minWidth }}>
          <TableHead>
            <TableRow>
              {columns.map((column) => (
                <TableCell key={column.header} sx={{ fontWeight: 600, textAlign: column.numeric ? 'right' : 'left' }}>
                  {column.header}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {shown.map((row, index) => (
              <TableRow key={rowKey(row, page * importPageSize + index)}>
                {columns.map((column) => (
                  <TableCell
                    key={column.header}
                    sx={{
                      verticalAlign: 'top',
                      overflowWrap: 'anywhere',
                      ...(column.numeric ? { ...numeric, textAlign: 'right', whiteSpace: 'nowrap' } : {}),
                    }}
                  >
                    {column.render(row)}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <PageNav noun={noun} total={rows.length} page={page} pageSize={importPageSize} onPage={setPage} />
      {listed && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
          {`Only the first ${formatNumber(rows.length)} of ${formatNumber(total)} ${noun} are listed.`}
        </Typography>
      )}
    </>
  );
}
