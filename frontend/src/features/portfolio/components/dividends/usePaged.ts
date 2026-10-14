import { useState } from 'react';
import { importPageSize } from '../../lib/dividendImport';

/** The page of a long list that is on screen; the page is kept inside the list when the list shrinks. */
export function usePaged<T>(rows: readonly T[]) {
  const [requested, setPage] = useState(0);
  const last = Math.max(0, Math.ceil(rows.length / importPageSize) - 1);
  const page = Math.min(requested, last);
  return {
    page,
    setPage,
    shown: rows.slice(page * importPageSize, (page + 1) * importPageSize),
  };
}
