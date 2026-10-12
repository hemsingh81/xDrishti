import { useCallback, useLayoutEffect, useRef, type FocusEvent, type KeyboardEvent } from 'react';

/** Marks the focusable data rows (skeleton rows do not have it). */
export const rowAttribute = 'data-grid-row';
/** Marks a row's selection checkbox input: arrow keys move between rows from it, Space and Enter stay its own. */
export const selectAttribute = 'data-grid-select';

const pageStep = 10;
const downKeys: ReadonlySet<string> = new Set(['ArrowDown', 'PageDown', 'End']);

interface Options {
  rowCount: number;
  onActivate: (index: number) => void;
  onToggle: (index: number) => void;
  /** Infinite lists: a key that moves down reached the last loaded row, so more rows are wanted. */
  onReachEnd?: () => void;
}

/**
 * Keyboard model of the rows: one tab stop (roving tabindex, kept in the DOM so a key press does not re-render the
 * table), Up/Down/Home/End/PageUp/PageDown move between rows, Enter activates the focused row and Space toggles its
 * selection. A downward key that lands on the last loaded row calls `onReachEnd` (infinite lists load more). Only
 * events whose target is the row itself (or, for arrows, its selection checkbox) are handled, and never with
 * Ctrl/Meta/Alt or when something already handled them: drag handles, inline editors, buttons and inputs inside cells
 * keep their own keys.
 */
export function useRowNavigation({ rowCount, onActivate, onToggle, onReachEnd }: Options) {
  const bodyRef = useRef<HTMLTableSectionElement>(null);
  const focused = useRef(0);

  const rowElements = useCallback(
    () => Array.from(bodyRef.current?.querySelectorAll<HTMLElement>(`:scope > [${rowAttribute}]`) ?? []),
    [],
  );
  const indexOf = (target: EventTarget) => {
    const row = target instanceof Element ? target.closest<HTMLElement>(`[${rowAttribute}]`) : null;
    return row ? rowElements().indexOf(row) : -1;
  };

  /** Exactly one row is a tab stop: the focused one (the first when nothing was focused yet). */
  const syncTabStops = useCallback(() => {
    const rows = rowElements();
    const current = Math.min(focused.current, Math.max(0, rows.length - 1));
    rows.forEach((row, index) => {
      const wanted = index === current ? 0 : -1;
      if (row.tabIndex !== wanted) row.tabIndex = wanted;
    });
  }, [rowElements]);

  // Rows come and go (paging, reloading): keep the single tab stop right after every render.
  useLayoutEffect(syncTabStops);

  function moveTo(index: number) {
    rowElements()[Math.max(0, Math.min(rowCount - 1, index))]?.focus();
  }

  function onKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
    const target = event.target as HTMLElement;
    const onRow = target.hasAttribute(rowAttribute);
    if (!onRow && !target.hasAttribute(selectAttribute)) return;
    const index = indexOf(target);
    if (index < 0) return;

    const moves = new Map<string, number>([
      ['ArrowDown', index + 1],
      ['ArrowUp', index - 1],
      ['PageDown', index + pageStep],
      ['PageUp', index - pageStep],
      ['Home', 0],
      ['End', rowCount - 1],
    ]);
    const next = moves.get(event.key);
    if (next !== undefined) {
      event.preventDefault();
      moveTo(next);
      if (next >= rowCount - 1 && downKeys.has(event.key)) onReachEnd?.();
    } else if (onRow && event.key === 'Enter') {
      event.preventDefault();
      onActivate(index);
    } else if (onRow && event.key === ' ') {
      event.preventDefault();
      onToggle(index);
    }
  }

  function onFocus(event: FocusEvent<HTMLElement>) {
    const index = indexOf(event.target);
    if (index >= 0) {
      focused.current = index;
      syncTabStops();
    }
  }

  /** Back to the first row as the tab stop (a new page or sort order is a new list). */
  const reset = useCallback(() => {
    focused.current = 0;
    syncTabStops();
  }, [syncTabStops]);

  return { bodyRef, onKeyDown, onFocus, reset };
}
