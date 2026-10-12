import { createContext, useContext } from 'react';
import type { CsvFileRow } from '@/shared/api';

/** What the cells of the review list need from the dialog: the choice, the limits and what a click does. */
export interface CsvListState {
  /** Names of the files that are checked. */
  selected: ReadonlySet<string>;
  /** The most a load may name is chosen: the others cannot be checked. */
  full: boolean;
  /** A job is running: Undo waits. */
  busy: boolean;
  onToggle: (row: CsvFileRow) => void;
  onUndo: (row: CsvFileRow) => void;
}

export const CsvListContext = createContext<CsvListState | null>(null);

/**
 * The checkbox and Undo cells read the choice from here instead of from the column definitions, so the columns stay the
 * same object while files are checked and only the checkboxes redraw, not 200 rows.
 */
export function useCsvList(): CsvListState {
  const state = useContext(CsvListContext);
  if (!state) throw new Error('useCsvList must be used inside the review list');
  return state;
}
