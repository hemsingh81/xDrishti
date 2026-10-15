import { useCallback, useEffect, useState } from 'react';
import {
  browserStorage,
  forgetIds,
  loadReadIds,
  markRead,
  readChangedEvent,
  readStorageKey,
  stateKey,
  withoutIds,
  type ReadStorage,
} from '../lib/readState';

interface ItemRef {
  id: string;
  since?: string | null;
}

/**
 * Read keys (`stateKey`: id plus `since`) kept in sync with storage. `storage` is injectable for tests; null disables persistence. Every user of the hook
 * (the bell, the pop-ups) sees a change made by another one, and by another tab of the browser.
 */
export function useReadState(storage: ReadStorage | null | undefined) {
  const resolved = storage === undefined ? browserStorage() : storage;
  const [readIds, setReadIds] = useState<ReadonlySet<string>>(() => loadReadIds(resolved));

  useEffect(() => {
    // Read ids only grow, so what another user of the hook wrote is added to what this one knows (storage may be off).
    const reload = () => {
      setReadIds((current) => new Set([...current, ...loadReadIds(resolved)]));
    };
    const onStorage = (event: StorageEvent) => {
      if (event.key === readStorageKey) reload();
    };
    window.addEventListener(readChangedEvent, reload);
    window.addEventListener('storage', onStorage);
    return () => {
      window.removeEventListener(readChangedEvent, reload);
      window.removeEventListener('storage', onStorage);
    };
  }, [resolved]);

  const markItemsRead = useCallback(
    (items: readonly ItemRef[]) => {
      setReadIds(markRead(resolved, items.map(stateKey)));
      window.dispatchEvent(new Event(readChangedEvent));
    },
    [resolved],
  );

  /** Drops the stored state (read and popped) of items that arrived resolved, so their next occurrence counts as new. */
  const forget = useCallback(
    (ids: readonly string[]) => {
      if (ids.length === 0) return;
      forgetIds(resolved, ids);
      setReadIds((current) => withoutIds(current, ids));
    },
    [resolved],
  );
  return { readIds, markItemsRead, forget };
}
