import {
  Box,
  ButtonBase,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { alpha } from '@mui/material/styles';
import { useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { numeric } from '@/theme';
import type { MetricState } from './metric';

/** Alpha of the hatching stripe (secondary text over the page background); the contrast of words on it is tested. */
export const HATCH_ALPHA = 0.15;

export interface HeatGridRow {
  id: string;
  label: string;
  /** A second line under the label. */
  detail?: string;
}

export interface HeatGridColumn {
  id: string;
  label: string;
}

export interface HeatGridCell {
  /** What `onOpen` receives. */
  key: string;
  /** The accessible name: the whole cell and its state in words, so it never depends on colour or hatching. */
  name: string;
  /** `insufficient` is hatched; only a `value` is shaded; every other state stays plain. */
  state: MetricState;
  /** Shade of a value cell: the palette colour and its alpha over the card. */
  shade?: { tone: 'success' | 'error'; alpha: number };
  /** What the tile prints (the words and figures). */
  content: ReactNode;
}

interface HeatGridProps {
  /** Names the grid. */
  label: string;
  /** Names the sideways-scrolling region around it. */
  regionLabel: string;
  /** Heading of the row-header column. */
  rowHeader: string;
  rows: readonly HeatGridRow[];
  columns: readonly HeatGridColumn[];
  /** The cell of a row and a column, or null where there is none. */
  cell: (rowId: string, columnId: string) => HeatGridCell | null;
  selectedKey?: string | null;
  onOpen: (key: string) => void;
  /** Printed where a row has no cell for a column. */
  emptyText?: string;
  /** Legend text above the grid. */
  legend?: ReactNode;
}

type At = readonly [row: number, column: number];

const refKey = (rowId: string, columnId: string) => `${rowId}\u0000${columnId}`;

/**
 * Rows by columns, one button per cell. A real grid (`role="grid"` with row, column and row headers) that scrolls sideways
 * inside its own focusable region and keeps the row-header column in view. One tab stop (roving): arrows move between cells,
 * Home and End to the ends of the row, Ctrl+Home and Ctrl+End to the ends of the grid; positions without a cell are skipped.
 */
export function HeatGrid({
  label,
  regionLabel,
  rowHeader,
  rows,
  columns,
  cell,
  selectedKey = null,
  onOpen,
  emptyText = 'No cell',
  legend,
}: HeatGridProps) {
  const refs = useRef(new Map<string, HTMLButtonElement>());
  const [active, setActive] = useState<At | null>(null);

  // Every position with a cell, in reading order.
  const positions = useMemo(
    () => rows.flatMap((row, r) => columns.flatMap((column, c) => (cell(row.id, column.id) ? [[r, c] as const] : []))),
    [rows, columns, cell],
  );
  const tabStop = positions.find((at) => at[0] === active?.[0] && at[1] === active[1]) ?? positions[0];

  function target(from: At, event: KeyboardEvent): At | undefined {
    const inRow = positions.filter((at) => at[0] === from[0]);
    const inColumn = positions.filter((at) => at[1] === from[1]);
    switch (event.key) {
      case 'ArrowRight':
        return inRow.find((at) => at[1] > from[1]);
      case 'ArrowLeft':
        return inRow.findLast((at) => at[1] < from[1]);
      case 'ArrowDown':
        return inColumn.find((at) => at[0] > from[0]);
      case 'ArrowUp':
        return inColumn.findLast((at) => at[0] < from[0]);
      case 'Home':
        return event.ctrlKey ? positions[0] : inRow[0];
      case 'End':
        return event.ctrlKey ? positions.at(-1) : inRow.at(-1);
      default:
        return undefined;
    }
  }

  function onKeyDown(event: KeyboardEvent, from: At) {
    const to = target(from, event);
    if (!to) return;
    event.preventDefault();
    setActive(to);
    const row = rows[to[0]];
    const column = columns[to[1]];
    if (row && column) refs.current.get(refKey(row.id, column.id))?.focus();
  }

  return (
    <Box>
      {legend && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          {legend}
        </Typography>
      )}
      <TableContainer role="region" aria-label={regionLabel} tabIndex={0}>
        <Table
          role="grid"
          size="small"
          aria-label={label}
          sx={{ '& tbody tr:nth-of-type(even)': { backgroundColor: 'transparent' } }}
        >
          <TableHead>
            <TableRow role="row">
              <TableCell role="columnheader" sx={{ position: 'sticky', left: 0, zIndex: 1 }}>
                {rowHeader}
              </TableCell>
              {columns.map((column) => (
                <TableCell key={column.id} role="columnheader">
                  {column.label}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row, r) => (
              <TableRow key={row.id} role="row">
                <TableCell
                  role="rowheader"
                  component="th"
                  scope="row"
                  sx={{ position: 'sticky', left: 0, zIndex: 1, backgroundColor: 'background.paper', minWidth: 120 }}
                >
                  <Typography component="span" variant="body2" sx={{ display: 'block', fontWeight: 600 }}>
                    {row.label}
                  </Typography>
                  {row.detail && (
                    <Typography component="span" variant="caption" color="text.secondary">
                      {row.detail}
                    </Typography>
                  )}
                </TableCell>
                {columns.map((column, c) => {
                  const item = cell(row.id, column.id);
                  const key = refKey(row.id, column.id);
                  return (
                    <TableCell key={column.id} role="gridcell" sx={{ verticalAlign: 'top', p: 0.5 }}>
                      {item ? (
                        <ButtonBase
                          ref={(element: HTMLButtonElement | null) => {
                            if (element) refs.current.set(key, element);
                            else refs.current.delete(key);
                          }}
                          onClick={() => {
                            setActive([r, c]);
                            onOpen(item.key);
                          }}
                          onKeyDown={(event) => {
                            onKeyDown(event, [r, c]);
                          }}
                          tabIndex={tabStop?.[0] === r && tabStop[1] === c ? 0 : -1}
                          aria-label={item.name}
                          aria-haspopup="dialog"
                          aria-expanded={item.key === selectedKey}
                          sx={(theme) => ({
                            display: 'block',
                            width: '100%',
                            minWidth: 128,
                            minHeight: 44,
                            textAlign: 'left',
                            p: 1,
                            borderRadius: 1,
                            border: 1,
                            borderColor: item.key === selectedKey ? 'primary.main' : 'divider',
                            color: 'text.primary',
                            fontSize: 'caption.fontSize',
                            ...numeric,
                            ...(item.state.kind === 'insufficient'
                              ? {
                                  backgroundColor: theme.palette.background.default,
                                  backgroundImage: `repeating-linear-gradient(135deg, transparent 0 6px, ${alpha(theme.palette.text.secondary, HATCH_ALPHA)} 6px 8px)`,
                                }
                              : {
                                  backgroundColor:
                                    item.state.kind === 'value' && item.shade
                                      ? alpha(theme.palette[item.shade.tone].main, item.shade.alpha)
                                      : theme.palette.background.paper,
                                }),
                          })}
                        >
                          {item.content}
                        </ButtonBase>
                      ) : (
                        <Typography variant="caption" color="text.secondary" sx={{ px: 1 }}>
                          {emptyText}
                        </Typography>
                      )}
                    </TableCell>
                  );
                })}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Box>
  );
}
