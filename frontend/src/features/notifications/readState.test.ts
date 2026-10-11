import { loadReadIds, markRead, readStorageKey, type ReadStorage } from './lib/readState';

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
});
