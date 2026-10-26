/**
 * Grid — the reusable list control of the app (instrument lists now; baskets, tracked instruments and audit lists
 * later). Import it by path (`@/shared/ui/grid`), not through the `@/shared/ui` barrel: it pulls MUI Table into the
 * chunk of the screens that use it, not into the initial bundle.
 *
 * Props (see `types.ts`):
 * - `columns` — id, header, `accessor` (text) or `render` (markup), `width` / `minWidth` / `grow`, `align`, `numeric`
 *   (right-aligned tabular figures), `sortable`, `hideBelow` (a breakpoint). `rows` + `getRowId`.
 * - `sort` / `onSortChange` — controlled; the sorted header carries `aria-sort`.
 * - `selectedIds` / `onSelectionChange` — optional, controlled; adds a checkbox column with select-all ("Select all loaded rows" with `infinite`)
 *   and shift-click ranges. `getRowLabel` names each row's checkbox.
 * - `onRowClick`, `activeRowId` — clicking (or Enter on) a row; the row that is open elsewhere stays highlighted.
 * - `infinite` — infinite scroll (the way every list loads further rows): `{ total, hasMore, loadingMore, onLoadMore,
 *   error?, onRetryMore? }`. The caller owns the loaded rows (see `useInfiniteRows` in `shared/lib`) and appends the next
 *   page on `onLoadMore`. A sentinel row at the end of the rows is observed (IntersectionObserver rooted at the scroll
 *   container, 400 px margin): near the viewport it calls `onLoadMore`, once per page and never while `loadingMore`, on
 *   `error` or while `loading`; every new page re-arms it, so a first page that does not fill the viewport keeps
 *   loading until it does or `hasMore` is false. While loading the sentinel row shows a small spinner; after a failure
 *   it shows "Could not load more rows" with Retry (`onRetryMore`) and auto-loading stops until it is pressed. The
 *   fixed footer says "Showing 100 of 3,204" (`aria-live="polite"`). `aria-rowcount` is the total; End, PageDown or
 *   Arrow Down landing on the last loaded row asks for more. Selection by id survives new pages.
 * - `resetKey` — identifies the list (filters, search). When it or `sort` changes the body scrolls back to the top and
 *   the first row is the tab stop again. Do not derive it from the rows: new pages must not move the reader.
 * - `loading` (skeleton rows; rows already shown stay, dimmed), `error` + `onRetry`, `emptyState`.
 * - `stickyHeader` (default), `stickyFirstColumn`, `density` (`compact` | `comfortable`).
 *
 * Layout: the grid fills its parent (a flex item with `min-height: 0`); the body between the header and the footer is
 * the scroll container, so give the parent a height (a flex column that fills the viewport) and the rows scroll while
 * the column titles and the footer stay. In a parent with no height the grid is simply as tall as its rows. It scrolls
 * sideways on its own when the columns do not fit.
 *
 * Keyboard: the rows are one tab stop (the checkboxes are not); Up/Down/Home/End/PageUp/PageDown move between rows,
 * Enter opens the row, Space toggles its selection. Only events from the row itself (or its checkbox, for arrows) are
 * handled, never with Ctrl/Meta/Alt, so drag handles, inline editors and buttons in cells keep their own keys. A new
 * sort order or `resetKey` scrolls the body back to the top and makes the first row the tab stop again.
 * Semantics: `grid` / `row` / `columnheader` / `gridcell` with `aria-rowindex` (position in the whole list),
 * `aria-colindex` / `aria-colcount`, `aria-multiselectable` with selection and `aria-busy` while loading.
 *
 * Reuse (basket members with drag and drop, bulk actions): a column with `kind: 'handle'` holds drag handles or row
 * menus: clicks in it never open the row and the keyboard handling ignores it. `getRowProps(row, index)` adds `sx`, a
 * `ref` (a sortable's node ref) and `data-*` / `aria-*` attributes to a row. Rows are memoised and the roving tabindex
 * lives in the DOM, so a key press or a selection re-renders only the rows that changed: pass a stable `columns` array,
 * and stable objects from `getRowProps`. No virtualisation: pages are 50-100 rows (an infinite list grows by one page at a time).
 */
import { Alert, Box, Button, Table, TableBody } from '@mui/material';
import { useCallback, useEffect, useRef } from 'react';
import { motion, stateAlpha, table } from '@/theme';
import { GridHeader } from './GridHeader';
import { GridInfiniteStatus } from './GridInfiniteStatus';
import { GridMoreRow } from './GridMoreRow';
import { GridRow } from './GridRow';
import { GridSkeletonRows } from './GridSkeletonRows';
import { useInfiniteLoad } from './useInfiniteLoad';
import { useRowNavigation } from './useRowNavigation';
import { useRowSelection } from './useRowSelection';
import type { GridProps } from './types';

export function Grid<Row>({
  label,
  columns,
  rows,
  getRowId,
  sort,
  onSortChange,
  selectedIds,
  onSelectionChange,
  getRowLabel,
  getRowProps,
  onRowClick,
  activeRowId = null,
  infinite,
  resetKey,
  loading = false,
  skeletonRowCount = 8,
  error = null,
  onRetry,
  emptyState,
  stickyHeader = true,
  stickyFirstColumn = false,
  density = 'compact',
}: GridProps<Row>) {
  const selection = useRowSelection({ rows, getRowId, selectedIds, onSelectionChange });
  const scroller = useRef<HTMLDivElement>(null);
  const stale = loading && rows.length > 0;
  const { sentinelRef, requestMore } = useInfiniteLoad({
    infinite,
    stale,
    rowCount: rows.length,
    resetKey,
    root: scroller,
  });
  const { bodyRef, onKeyDown, onFocus, reset } = useRowNavigation({
    rowCount: rows.length,
    onReachEnd: requestMore,
    onActivate: (index) => {
      const row = rows[index];
      if (row !== undefined) onRowClick?.(row);
    },
    onToggle: (index) => {
      if (selection.enabled) selection.toggleRow(index);
    },
  });

  // Rows get callbacks that never change identity (they read the latest props), so they can stay memoised.
  const latest = useRef({ onRowClick, setRow: selection.setRow });
  useEffect(() => {
    latest.current = { onRowClick, setRow: selection.setRow };
  });
  const handleRowClick = useCallback((row: Row) => {
    latest.current.onRowClick?.(row);
  }, []);
  const handleSelect = useCallback((index: number, checked: boolean, range: boolean) => {
    latest.current.setRow(index, checked, range);
  }, []);

  // A new sort order or list (`resetKey`) is a new list: back to the top, first row is the tab stop.
  const sortColumn = sort?.columnId;
  const sortDescending = sort?.descending;
  useEffect(() => {
    if (scroller.current) scroller.current.scrollTop = 0;
    reset();
  }, [sortColumn, sortDescending, resetKey, reset]);

  const firstLoad = loading && rows.length === 0;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 0, minWidth: 0 }}>
      <Box
        ref={scroller}
        role="region"
        aria-label={`${label}, scrolls sideways`}
        sx={{
          flex: '1 1 auto',
          minHeight: 0,
          overflow: 'auto',
          // Keyboard focus scrolls a row clear of the sticky header.
          scrollPaddingTop: `${table.rowHeight}px`,
          opacity: stale ? stateAlpha.busy : 1,
          // Reduced motion is switched off globally (theme/globalStyles).
          transition: `opacity ${motion.fast}`,
        }}
      >
        {error !== null ? (
          <Alert
            severity="error"
            sx={{ m: 2 }}
            action={
              onRetry && (
                <Button color="inherit" size="small" onClick={onRetry}>
                  Retry
                </Button>
              )
            }
          >
            {error}
          </Alert>
        ) : (
          <Table
            role="grid"
            size={density === 'compact' ? 'small' : 'medium'}
            stickyHeader={stickyHeader}
            aria-label={label}
            aria-busy={loading}
            aria-multiselectable={selection.enabled ? true : undefined}
            aria-rowcount={(infinite ? Math.max(infinite.total, rows.length) : rows.length) + 1}
            aria-colcount={columns.length + (selection.enabled ? 1 : 0)}
          >
            <GridHeader
              columns={columns}
              sort={sort}
              onSortChange={onSortChange}
              stickyFirstColumn={stickyFirstColumn}
              selection={
                selection.enabled
                  ? {
                      allOnPage: selection.allOnPage,
                      someOnPage: selection.someOnPage,
                      disabled: rows.length === 0,
                      label: infinite ? 'Select all loaded rows' : 'Select all rows',
                      onChange: selection.setAllOnPage,
                    }
                  : null
              }
            />
            <TableBody ref={bodyRef} onKeyDown={onKeyDown} onFocus={onFocus}>
              {firstLoad && (
                <GridSkeletonRows columns={columns} count={skeletonRowCount} selectable={selection.enabled} />
              )}
              {rows.map((row, index) => {
                const id = getRowId(row);
                return (
                  <GridRow
                    key={id}
                    row={row}
                    index={index}
                    rowIndex={index + 2}
                    columns={columns}
                    density={density}
                    clickable={onRowClick !== undefined}
                    active={id === activeRowId}
                    selected={selection.enabled ? selection.isSelected(id) : undefined}
                    rowLabel={getRowLabel?.(row) ?? `row ${String(index + 1)}`}
                    stickyFirstColumn={stickyFirstColumn}
                    extras={getRowProps?.(row, index)}
                    onRowClick={handleRowClick}
                    onSelect={handleSelect}
                  />
                );
              })}
              {infinite && (infinite.hasMore || infinite.loadingMore || (infinite.error ?? null) !== null) && (
                <GridMoreRow
                  infinite={infinite}
                  colSpan={columns.length + (selection.enabled ? 1 : 0)}
                  rowIndex={rows.length + 2}
                  sentinelRef={sentinelRef}
                />
              )}
            </TableBody>
          </Table>
        )}
        {error === null && !loading && rows.length === 0 && emptyState}
      </Box>
      {infinite && error === null && (
        <GridInfiniteStatus shown={rows.length} total={infinite.total} hidden={firstLoad} />
      )}
    </Box>
  );
}
