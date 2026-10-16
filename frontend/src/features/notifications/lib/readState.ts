/** Which notifications the owner has read: a per-browser convenience, so it lives in localStorage and may be absent. */
export const readStorageKey = 'xd.notifications.read';
/** Which attention items already popped up in this browser (kept apart from "read": a pop-up does not read the item). */
export const poppedStorageKey = 'xd.notifications.popped';
/** Old ids are dropped beyond this many, so the list cannot grow without bound. */
const maxStored = 200;

export type ReadStorage = Pick<Storage, 'getItem' | 'setItem'>;

/**
 * What identifies an item in the read and popped sets: its id plus the time the situation began (`since`). The server re-raises
 * a resolved item under the same id with a new `since`, which must count (and pop up) again. An item without `since` is keyed
 * by its plain id, which is also how ids were stored before this existed, so old storage still matches those items.
 */
export const stateKey = (item: { id: string; since?: string | null }): string =>
  item.since ? `${item.id}|${item.since}` : item.id;

/** True when a stored entry belongs to this id: the plain id or `id|since`. */
const belongsTo = (entry: string, id: string): boolean => entry === id || entry.startsWith(`${id}|`);

/** The browser's storage, or null when it is unavailable (blocked, private mode). */
export function browserStorage(): ReadStorage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

function loadIdSet(storage: ReadStorage | null, key: string): Set<string> {
  try {
    const parsed: unknown = JSON.parse(storage?.getItem(key) ?? '[]');
    return new Set(Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === 'string') : []);
  } catch {
    return new Set();
  }
}

/** Adds `ids` to the stored set (newest kept when capped) and returns the new set. Never throws. */
function addToIdSet(storage: ReadStorage | null, key: string, ids: readonly string[]): Set<string> {
  const next = loadIdSet(storage, key);
  for (const id of ids) {
    // Delete first so a re-marked id moves to the newest position and survives the cap.
    next.delete(id);
    next.add(id);
  }
  const kept = [...next].slice(-maxStored);
  try {
    storage?.setItem(key, JSON.stringify(kept));
  } catch {
    // Storage full or blocked: the state then lasts only for this page load.
  }
  return new Set(kept);
}

export const loadReadIds = (storage: ReadStorage | null): Set<string> => loadIdSet(storage, readStorageKey);

/** Adds `ids` to the stored read set (newest kept when capped) and returns the new set. Never throws. */
export const markRead = (storage: ReadStorage | null, ids: readonly string[]): Set<string> =>
  addToIdSet(storage, readStorageKey, ids);

export const loadPoppedIds = (storage: ReadStorage | null): Set<string> => loadIdSet(storage, poppedStorageKey);

/** Remembers that these items popped up, so they do not pop up again after a reload. Never throws. */
export const markPopped = (storage: ReadStorage | null, ids: readonly string[]): Set<string> =>
  addToIdSet(storage, poppedStorageKey, ids);

/** Fired on `window` after the read ids changed, so the bell and the pop-ups (separate hooks) see the same state. */
export const readChangedEvent = 'xd:notifications-read';

/**
 * Drops everything stored for these ids (read and popped, whichever `since`), once their items have arrived resolved: the state
 * of a finished situation must not hide the next one. Writes only when something is removed. Never throws.
 */
export function forgetIds(storage: ReadStorage | null, ids: readonly string[]): void {
  for (const key of [readStorageKey, poppedStorageKey]) {
    const stored = loadIdSet(storage, key);
    const kept = [...stored].filter((entry) => !ids.some((id) => belongsTo(entry, id)));
    if (kept.length === stored.size) continue;
    try {
      storage?.setItem(key, JSON.stringify(kept));
    } catch {
      // Storage full or blocked: nothing more to do.
    }
  }
}

/** The set without the entries of these ids; the same set when there is nothing to remove. */
export function withoutIds(set: ReadonlySet<string>, ids: readonly string[]): ReadonlySet<string> {
  const kept = [...set].filter((entry) => !ids.some((id) => belongsTo(entry, id)));
  return kept.length === set.size ? set : new Set(kept);
}
