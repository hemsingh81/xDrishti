import { useState } from 'react';
import type { CsvFileRow } from '@/shared/api';
import { isSelectable, maxLoadFiles } from '../lib/csvReview';

/**
 * The files checked in the review dialog, in the order they were checked (the preview reads the first ones). They are
 * kept as rows, so the choice survives a change of page, search or filter; only mapped files can be checked, up to
 * what one load may name.
 */
export function useCsvSelection() {
  const [chosen, setChosen] = useState<ReadonlyMap<string, CsvFileRow>>(new Map());

  const set = (rows: readonly CsvFileRow[], on: boolean) => {
    setChosen((previous) => {
      const next = new Map(previous);
      for (const row of rows) {
        if (!on) next.delete(row.name);
        else if (isSelectable(row) && next.size < maxLoadFiles) next.set(row.name, row);
      }
      return next;
    });
  };

  const files = [...chosen.values()];
  return {
    files,
    names: new Set(chosen.keys()),
    full: chosen.size >= maxLoadFiles,
    toggle: (row: CsvFileRow) => {
      set([row], !chosen.has(row.name));
    },
    set,
    clear: () => {
      setChosen(new Map());
    },
  };
}
