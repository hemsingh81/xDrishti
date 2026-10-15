import {
  forgetIds,
  loadPoppedIds,
  loadReadIds,
  markPopped,
  markRead,
  poppedStorageKey,
  readStorageKey,
  stateKey,
  withoutIds,
  type ReadStorage,
} from './lib/readState';

class FakeStorage implements ReadStorage {
  data = new Map<string, string>();
  getItem = (key: string) => this.data.get(key) ?? null;
  setItem = (key: string, value: string) => {
    this.data.set(key, value);
  };
}

describe('read state', () => {
  it('keeps the newest 200 ids, and a re-marked old id counts as newest', () => {
    const storage = new FakeStorage();
    const ids = Array.from({ length: 200 }, (_, i) => `id-${String(i)}`);
    markRead(storage, ids);

    markRead(storage, ['id-0']); // seen again: moves to the newest position
    markRead(storage, ['fresh']); // pushes the oldest out

    const kept = loadReadIds(storage);
    expect(kept.size).toBe(200);
    expect(kept.has('id-0')).toBe(true);
    expect(kept.has('fresh')).toBe(true);
    expect(kept.has('id-1')).toBe(false);
  });

  it('survives broken or unavailable storage', () => {
    const storage = new FakeStorage();
    storage.setItem(readStorageKey, '{not json');
    expect(loadReadIds(storage).size).toBe(0);
    expect(markRead(null, ['a']).has('a')).toBe(true);
  });

  it('tells situations apart by id plus time, and keeps a plain id for an item without time', () => {
    expect(stateKey({ id: 'a', since: '2026-10-14T09:30:00Z' })).toBe('a|2026-10-14T09:30:00Z');
    expect(stateKey({ id: 'a', since: null })).toBe('a');
    expect(stateKey({ id: 'a' })).toBe('a');
  });

  it('forgets what was stored for an id, whatever its time, and nothing else', () => {
    const storage = new FakeStorage();
    markRead(storage, ['a', 'a|t1', 'a|t2', 'b|t1']);
    markPopped(storage, ['a|t1', 'ab|t1']);

    forgetIds(storage, ['a']);

    expect([...loadReadIds(storage)]).toEqual(['b|t1']);
    expect([...loadPoppedIds(storage)]).toEqual(['ab|t1']);
  });

  it('writes nothing when there is nothing to forget', () => {
    const storage = new FakeStorage();
    markRead(storage, ['b']);
    const writes = vi.spyOn(storage, 'setItem');

    forgetIds(storage, ['a']);

    expect(writes).not.toHaveBeenCalled();
    expect(storage.data.has(poppedStorageKey)).toBe(false);
    const set = new Set(['b']);
    expect(withoutIds(set, ['a'])).toBe(set);
    expect([...withoutIds(new Set(['a|t', 'b']), ['a'])]).toEqual(['b']);
  });
});
