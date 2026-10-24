import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import type { ReactNode } from 'react';
import { EvidenceBlock, type EvidenceProps } from './EvidenceBlock';
import { MetricCell } from './MetricCell';
import type { MetricState } from './metric';
import type { MetricValueProps } from './MetricValue';
import { NonValue } from './NonValue';

export type Column<Row> =
  /** A label or any node; the first column of this kind is the row header. */
  | { kind: 'text'; id: string; header: string; render: (row: Row) => ReactNode; align?: 'left' | 'right' }
  /** A figure in one of its honest states, right-aligned. */
  | ({
      kind: 'metric';
      id: string;
      header: string;
      metric: (row: Row) => MetricState;
      n?: (row: Row) => number | null | undefined;
    } & Pick<MetricValueProps, 'format' | 'tone'>);

/** What the whole table is when it has no rows to show: still loading, failed, or nothing yet. */
export type TableStatus = Extract<MetricState, { kind: 'loading' | 'error' | 'empty' }>;

interface DataTableProps<Row> {
  /** Names the table and the scrollable region around it. */
  label: string;
  columns: readonly Column<Row>[];
  rows: readonly Row[];
  rowKey: (row: Row) => string;
  /** Loading, error or empty replace the body with that state in words; a table never shows a header over blank space. */
  status?: TableStatus;
  /** The evidence under the table (n, range, basis, limits). Pass it for any table of figures. */
  evidence?: Omit<EvidenceProps, 'compact'>;
}

/**
 * A table of figures. It scrolls sideways inside its own region (keyboard reachable), keeps a real header and row headers,
 * draws every figure through `MetricCell` (so a non-value shows its word, never a digit), and says "No records yet" instead
 * of showing no rows.
 */
export function DataTable<Row>({ label, columns, rows, rowKey, status, evidence }: DataTableProps<Row>) {
  const state: TableStatus | undefined = status ?? (rows.length === 0 ? { kind: 'empty' } : undefined);
  const rowHeader = columns.findIndex((c) => c.kind === 'text');
  return (
    <Box>
      <TableContainer role="region" aria-label={`${label}, scrolls sideways`} tabIndex={0}>
        <Table size="small" aria-label={label} aria-busy={status?.kind === 'loading'}>
          <TableHead>
            <TableRow>
              {columns.map((c) => (
                <TableCell key={c.id} align={c.kind === 'metric' || c.align === 'right' ? 'right' : 'left'}>
                  {c.header}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {state ? (
              <TableRow>
                <TableCell colSpan={columns.length}>
                  <NonValue state={state} />
                </TableCell>
              </TableRow>
            ) : (
              rows.map((row) => (
                <TableRow key={rowKey(row)}>
                  {columns.map((c, index) =>
                    c.kind === 'metric' ? (
                      <MetricCell key={c.id} state={c.metric(row)} format={c.format} tone={c.tone} n={c.n?.(row)} />
                    ) : (
                      <TableCell
                        key={c.id}
                        align={c.align ?? 'left'}
                        {...(index === rowHeader ? { component: 'th', scope: 'row' } : {})}
                      >
                        {c.render(row)}
                      </TableCell>
                    ),
                  )}
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </TableContainer>
      {evidence && (
        <Box sx={{ mt: 1 }}>
          <EvidenceBlock {...evidence} />
        </Box>
      )}
    </Box>
  );
}
