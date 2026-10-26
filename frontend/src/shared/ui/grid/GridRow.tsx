import { alpha } from '@mui/material/styles';
import { Checkbox, TableCell, TableRow } from '@mui/material';
import { memo } from 'react';
import { focusRing, table } from '@/theme';
import { touchTarget } from '@/theme/tokens';
import { cellSx, selectionCellSx } from './cellStyle';
import { rowAttribute, selectAttribute } from './useRowNavigation';
import type { GridColumn, GridDensity, GridRowExtras } from './types';

interface GridRowProps<Row> {
  row: Row;
  index: number;
  /** 1-based position in the whole list (the header row is 1), for `aria-rowindex`. */
  rowIndex: number;
  columns: readonly GridColumn<Row>[];
  density: GridDensity;
  clickable: boolean;
  active: boolean;
  /** Undefined when the grid has no selection. */
  selected: boolean | undefined;
  rowLabel: string;
  stickyFirstColumn: boolean;
  extras: GridRowExtras | undefined;
  onRowClick: (row: Row) => void;
  onSelect: (index: number, checked: boolean, range: boolean) => void;
}

const cellContent = <Row,>(column: GridColumn<Row>, row: Row) => column.render?.(row) ?? column.accessor?.(row) ?? null;
const selectionInput = (label: string) => ({ 'aria-label': `Select ${label}`, tabIndex: -1, [selectAttribute]: '' });
const stop = (event: { stopPropagation: () => void }) => {
  event.stopPropagation();
};

/**
 * One data row: focusable (the grid keeps one tab stop), clickable, optionally with its checkbox. Memoised: it
 * re-renders only when its own row, selection, highlight or extras change, so the callbacks it gets must be stable.
 */
function GridRowView<Row>({
  row,
  index,
  rowIndex,
  columns,
  density,
  clickable,
  active,
  selected,
  rowLabel,
  stickyFirstColumn,
  extras,
  onRowClick,
  onSelect,
}: GridRowProps<Row>) {
  const height = density === 'comfortable' ? table.rowHeightComfortable : table.rowHeight;
  const selectable = selected !== undefined;
  return (
    <TableRow
      {...extras?.attributes}
      ref={extras?.ref}
      {...{ [rowAttribute]: '' }}
      role="row"
      aria-rowindex={rowIndex}
      tabIndex={index === 0 ? 0 : -1}
      hover={clickable}
      selected={active || selected === true}
      aria-selected={selected}
      aria-current={active ? 'true' : undefined}
      onClick={() => {
        onRowClick(row);
      }}
      sx={[
        (theme) => ({
          cursor: clickable ? 'pointer' : 'default',
          // One source for the row's tint: `--grid-row-tint` (zebra, hover, selected). The row paints it and so do the
          // opaque sticky cells, so the theme's own row backgrounds are replaced by it (same selectors, later rule).
          '--grid-row-tint': 'transparent',
          '&, &:nth-of-type(even), &.MuiTableRow-hover:hover, &.Mui-selected, &.Mui-selected:hover': {
            backgroundColor: 'var(--grid-row-tint)',
          },
          // Alternate rows carry a faint tint (as the theme's table rows do), also under a sticky first column.
          '&:nth-of-type(even)': { '--grid-row-tint': alpha(theme.palette.background.alt, table.zebraAlpha) },
          // Only a clickable row reacts to the pointer; otherwise the zebra tint stays.
          ...(clickable
            ? { '&:hover': { '--grid-row-tint': alpha(theme.palette.primary.main, table.hoverAlpha) } }
            : {}),
          '&.Mui-selected, &.Mui-selected:hover': {
            '--grid-row-tint': alpha(theme.palette.primary.main, table.selectedAlpha),
          },
          '&:focus-visible': {
            outline: `${focusRing.width}px solid ${theme.palette.primary.main}`,
            outlineOffset: -focusRing.width,
          },
          '& > td': { height },
          // A row that opens something is a touch target: 44 px on a phone (rows that do nothing stay dense).
          ...(clickable
            ? { [`@media (max-width:${touchTarget.phoneMaxWidth}px)`]: { '& > td': { height: touchTarget.phone } } }
            : {}),
        }),
        extras?.sx ?? false,
      ]}
    >
      {selectable && (
        <TableCell
          role="gridcell"
          aria-colindex={1}
          padding="checkbox"
          sx={selectionCellSx(stickyFirstColumn, false)}
          onClick={stop}
        >
          <Checkbox
            size="small"
            checked={selected}
            onChange={(event) => {
              const shift = 'shiftKey' in event.nativeEvent && event.nativeEvent.shiftKey === true;
              onSelect(index, event.target.checked, shift);
            }}
            // The row is the tab stop (Space toggles it); the box is for the pointer and the screen reader.
            slotProps={{ input: selectionInput(rowLabel) }}
          />
        </TableCell>
      )}
      {columns.map((column, position) => (
        <TableCell
          key={column.id}
          role="gridcell"
          aria-colindex={position + (selectable ? 2 : 1)}
          onClick={column.kind === 'handle' ? stop : undefined}
          sx={cellSx(column, { sticky: stickyFirstColumn && position === 0, selectable, head: false })}
        >
          {cellContent(column, row)}
        </TableCell>
      ))}
    </TableRow>
  );
}

export const GridRow = memo(GridRowView) as typeof GridRowView;
