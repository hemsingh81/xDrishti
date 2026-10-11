import { useRef } from 'react';

interface Options<Row> {
  rows: readonly Row[];
  getRowId: (row: Row) => string;
  selectedIds: ReadonlySet<string> | undefined;
  onSelectionChange: ((ids: ReadonlySet<string>) => void) | undefined;
}

const none: ReadonlySet<string> = new Set();

/**
 * Selection over the rows on the page. The selected ids are controlled by the caller; this adds the gestures: toggle a
 * row, shift-click a range (from the last toggled row, in the state the clicked row gets) and select-all-on-page, which
 * leaves selections on other pages alone.
 */
export function useRowSelection<Row>({ rows, getRowId, selectedIds, onSelectionChange }: Options<Row>) {
  const selected = selectedIds ?? none;
  const anchorId = useRef<string | null>(null);

  const pageIds = rows.map(getRowId);
  const selectedOnPage = pageIds.filter((id) => selected.has(id)).length;

  function commit(changes: readonly string[], checked: boolean) {
    if (!onSelectionChange) return;
    const next = new Set(selected);
    for (const id of changes) {
      if (checked) next.add(id);
      else next.delete(id);
    }
    onSelectionChange(next);
  }

  /** Sets the row at `index` to `checked`; with `range`, every row between it and the last toggled row too. */
  function setRow(index: number, checked: boolean, range: boolean) {
    const id = pageIds[index];
    if (id === undefined) return;
    const anchor = anchorId.current === null ? -1 : pageIds.indexOf(anchorId.current);
    if (range && anchor >= 0) {
      commit(pageIds.slice(Math.min(anchor, index), Math.max(anchor, index) + 1), checked);
    } else {
      commit([id], checked);
    }
    anchorId.current = id;
  }

  return {
    enabled: onSelectionChange !== undefined,
    isSelected: (id: string) => selected.has(id),
    allOnPage: pageIds.length > 0 && selectedOnPage === pageIds.length,
    someOnPage: selectedOnPage > 0 && selectedOnPage < pageIds.length,
    setRow,
    toggleRow: (index: number) => {
      const id = pageIds[index];
      if (id !== undefined) setRow(index, !selected.has(id), false);
    },
    setAllOnPage: (checked: boolean) => {
      commit(pageIds, checked);
      anchorId.current = null;
    },
  };
}
