/** Which notifications the owner has read: a per-browser convenience, so it lives in localStorage and may be absent. */
export const readStorageKey = 'xd.notifications.read';
/** Old ids are dropped beyond this many, so the list cannot grow without bound. */
const maxStored = 200;

export type ReadStorage = Pick<Storage, 'getItem' | 'setItem'>;

/** The browser's storage, or null when it is unavailable (blocked, private mode). */
export function browserStorage(): ReadStorage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export function loadReadIds(storage: ReadStorage | null): Set<string> {
  try {
    const parsed: unknown = JSON.parse(storage?.getItem(readStorageKey) ?? '[]');
    return new Set(Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === 'string') : []);
  } catch {
    return new Set();
  }
}

/** Adds `ids` to the stored read set (newest kept when capped) and returns the new set. Never throws. */
export function markRead(storage: ReadStorage | null, ids: readonly string[]): Set<string> {
  const next = loadReadIds(storage);
  for (const id of ids) {
    // Delete first so a re-marked id moves to the newest position and survives the cap.
    next.delete(id);
    next.add(id);
  }
  const kept = [...next].slice(-maxStored);
  try {
    storage?.setItem(readStorageKey, JSON.stringify(kept));
  } catch {
    // Storage full or blocked: the read state then lasts only for this page load.
  }
  return new Set(kept);
}
