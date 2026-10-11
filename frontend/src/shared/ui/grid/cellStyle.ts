import type { SxProps, Theme } from '@mui/material/styles';
import { numeric } from '@/theme';
import type { GridColumn } from './types';

/** Width of the checkbox column (px); a sticky first column sits right after it. */
export const selectionColumnWidth = 48;

interface Options {
  /** The first data column stays put while scrolling sideways. */
  sticky: boolean;
  /** The checkbox column is present, so a sticky column starts after it. */
  selectable: boolean;
  /** The header cell (above the body cells when they overlap). */
  head: boolean;
}

/** Layout of one cell (header or body) of a column: alignment, sizing, responsive hiding and the sticky offset. */
export function cellSx<Row>(column: GridColumn<Row>, { sticky, selectable, head }: Options): SxProps<Theme> {
  const handle = column.kind === 'handle';
  const align = column.align ?? (column.numeric ? 'right' : 'left');
  return {
    whiteSpace: 'nowrap',
    paddingInline: 1.25,
    textAlign: align,
    ...(column.numeric ? numeric : {}),
    ...(handle
      ? { width: selectionColumnWidth, minWidth: selectionColumnWidth, paddingInline: 0, textAlign: 'center' }
      : {}),
    ...(column.width !== undefined ? { width: column.width, minWidth: column.width } : {}),
    ...(column.minWidth !== undefined ? { minWidth: column.minWidth } : {}),
    ...(column.grow ? { width: '100%' } : {}),
    ...(column.hideBelow ? { display: { xs: 'none', [column.hideBelow]: 'table-cell' } } : {}),
    ...(sticky
      ? {
          position: 'sticky',
          left: selectable ? selectionColumnWidth : 0,
          zIndex: head ? 3 : 1,
          // Body cells: opaque, with the row's tint on top, so scrolled cells do not show through. Header cells keep the
          // theme's header tint (already opaque).
          ...(head
            ? {}
            : {
                backgroundColor: 'background.paper',
                backgroundImage: 'linear-gradient(var(--grid-row-tint), var(--grid-row-tint))',
              }),
        }
      : {}),
  };
}

/** The checkbox column; it can stay put (left edge) together with a sticky first data column. */
export function selectionCellSx(sticky: boolean, head: boolean): SxProps<Theme> {
  return {
    width: selectionColumnWidth,
    minWidth: selectionColumnWidth,
    ...(sticky
      ? {
          position: 'sticky',
          left: 0,
          zIndex: head ? 3 : 1,
          ...(head
            ? {}
            : {
                backgroundColor: 'background.paper',
                backgroundImage: 'linear-gradient(var(--grid-row-tint), var(--grid-row-tint))',
              }),
        }
      : {}),
  };
}
