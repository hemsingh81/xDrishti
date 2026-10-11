import { useCallback, useState } from 'react';
import { browserStorage, loadReadIds, markRead, type ReadStorage } from '../lib/readState';

/** Read ids kept in sync with storage. `storage` is injectable for tests; null disables persistence. */
export function useReadState(storage: ReadStorage | null | undefined) {
  const resolved = storage === undefined ? browserStorage() : storage;
  const [readIds, setReadIds] = useState(() => loadReadIds(resolved));
  const markIdsRead = useCallback(
    (ids: readonly string[]) => {
      setReadIds(markRead(resolved, ids));
    },
    [resolved],
  );
  return { readIds, markIdsRead };
}
