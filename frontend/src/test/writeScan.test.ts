import { describe, expect, it } from 'vitest';
import { findWrites } from './writeScan';

describe('findWrites', () => {
  it.each([
    ["fetch(url, { method: 'post' })", 'method property'],
    ["await api.PUT('/x', {})", 'api write call'],
    ["await api . delete('/x')", 'api write call'],
    ['fetch(url, { method: verb })', 'fetch that is not GET'],
    ['fetch(url)', 'fetch that is not GET'],
    ["navigator.sendBeacon('/x', data)", 'sendBeacon'],
    ['const m = useMutation({})', 'useMutation'],
    ['ensureOk(result)', 'ensureOk'],
    ["const r = new XMLHttpRequest(); r.open('GET')", 'XMLHttpRequest'],
    ["new WebSocket('wss://x')", 'WebSocket'],
    ["await connection.invoke('PlaceThing')", 'hub write'],
  ])('flags %s', (text, expected) => {
    expect(findWrites(text)).toContain(expected);
  });

  it('lets reads through', () => {
    expect(findWrites("await api.GET('/api/x'); fetch(u, { method: 'GET' }); const ok = true;")).toEqual([]);
  });
});
