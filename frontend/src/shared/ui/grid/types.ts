import type { SxProps, Theme } from '@mui/material/styles';
import type { ReactNode, Ref } from 'react';

/** Breakpoint names of the theme; a column can be hidden below one of them. */
export type GridBreakpoint = 'sm' | 'md' | 'lg' | 'xl';

export interface GridColumn<Row> {
  /** Stable id; also the id reported by `onSortChange`. */
  id: string;
  /** Column title (plain text: it is the accessible name of the column header). */
  header: string;
  /**
   * `'handle'`: a column of drag handles, row menus or inline editors. Clicks inside it never open the row and the grid's
   * keyboard handling ignores events from it. Defaults to `'data'`.
   */
  kind?: 'data' | 'handle';
  /** The cell's value when it is plain text or a number. */
  accessor?: (row: Row) => ReactNode;
  /** The cell's content when it needs markup (chips, links); wins over `accessor`. */
  render?: (row: Row) => ReactNode;
  /** Fixed width (px number or any CSS length). Without it the column sizes to its content. */
  width?: number | string;
  minWidth?: number;
  /** The column absorbs the width the other columns leave over (the flex-grow of a table). */
  grow?: boolean;
  align?: 'left' | 'right' | 'center';
  /** Right-aligned tabular figures. */
  numeric?: boolean;
  sortable?: boolean;
  /** Hidden below this breakpoint (header and cells), e.g. `'md'` hides the column on phones and small tablets. */
  hideBelow?: GridBreakpoint;
}

export interface GridSort {
  columnId: string;
  descending: boolean;
}

/** Infinite-scroll mode: the caller owns the loaded rows and appends the next page when asked. */
export interface GridInfinite {
  /** Rows in the whole list (all pages); drives "Showing N of total" and `aria-rowcount`. */
  total: number;
  /** More rows can be loaded. Keep it false while the rows shown are a stale list of another filter. */
  hasMore: boolean;
  /** The next page is being fetched: shows the spinner row and pauses auto-loading. */
  loadingMore: boolean;
  /** Asked when the end of the loaded rows nears the viewport, or End / PageDown / Arrow Down reaches the last row. */
  onLoadMore: () => void;
  /** The last "load more" failed: an inline "Could not load more rows — Retry" row replaces the spinner. */
  error?: string | null;
  /** Retry button of that row; the grid stops auto-loading until it is pressed. */
  onRetryMore?: () => void;
}

/** What a caller can add to one row element (P1-15: a drag-and-drop sortable ref and styles; tests: data attributes). */
export interface GridRowExtras {
  /** An object or a theme function (not an array). */
  sx?: Exclude<SxProps<Theme>, readonly unknown[]>;
  ref?: Ref<HTMLTableRowElement>;
  /** Extra attributes, e.g. `{ 'data-row-kind': 'member' }`. Cannot replace the grid's own roles and handlers. */
  attributes?: Readonly<Record<`data-${string}` | `aria-${string}`, string | number | boolean | undefined>>;
}

export type GridDensity = 'compact' | 'comfortable';

export interface GridProps<Row> {
  /** Accessible name of the grid. */
  label: string;
  columns: readonly GridColumn<Row>[];
  rows: readonly Row[];
  getRowId: (row: Row) => string;

  sort?: GridSort | null;
  onSortChange?: (sort: GridSort) => void;

  /** Selection is on when `onSelectionChange` is given; `selectedIds` is then the controlled value. */
  selectedIds?: ReadonlySet<string>;
  onSelectionChange?: (ids: ReadonlySet<string>) => void;
  /** Accessible name of a row's checkbox, e.g. its symbol. */
  getRowLabel?: (row: Row) => string;

  /** Click, or Enter on the focused row. */
  onRowClick?: (row: Row) => void;
  /**
   * Per-row extras. Rows are memoised: return a stable object per row (or `undefined`), otherwise every row re-renders
   * whenever the grid does.
   */
  getRowProps?: (row: Row, index: number) => GridRowExtras | undefined;
  /** The row that is open elsewhere (a drawer): highlighted and marked `aria-current`. */
  activeRowId?: string | null;

  /**
   * Identifies the list (filters, search, sort): when it changes the grid scrolls back to the top and the first row
   * becomes the tab stop again. A change of `sort` does this too, so only pass what is not the sort. Not the rows: more
   * rows arriving, or a refresh, must not move the reader.
   */
  resetKey?: string | number;

  /** Infinite-scroll mode: the list loads further rows as it is scrolled. Without it the list is plain. */
  infinite?: GridInfinite;

  /** First load: skeleton rows. While rows are already shown (next page) they stay, dimmed. */
  loading?: boolean;
  skeletonRowCount?: number;
  /** Shown instead of the rows; `onRetry` adds a Retry button. */
  error?: string | null;
  onRetry?: () => void;
  /** Shown when there are no rows (and no error, not loading). */
  emptyState?: ReactNode;

  /** Header row stays visible while the body scrolls. Default true. */
  stickyHeader?: boolean;
  /** The first data column stays visible while scrolling sideways. */
  stickyFirstColumn?: boolean;
  density?: GridDensity;
}
