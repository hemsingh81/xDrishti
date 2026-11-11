import { describe, expect, it } from 'vitest';
import { findWrites } from '@/test/writeScan';

// BK-07 criterion 14 (P8-00 D6): a read-only screen has no way to write. The feature's own sources (not its tests) contain no
// mutation, no call that is not a GET, no import of another feature and no way to turn a name into markup.
const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

describe('the basket profile feature is read only', () => {
  const files = Object.entries(sources);

  it('finds its own sources', () => {
    expect(files.length).toBeGreaterThan(15);
    expect(files.some(([name]) => name.endsWith('profileQueries.ts'))).toBe(true);
  });

  it.each(files)('%s has no write call, no mutation and no other feature', (_name, text) => {
    expect(findWrites(text)).toEqual([]);
    expect(text.match(/@\/features\//g) ?? []).toEqual([]);
  });

  it.each(files)('%s renders no markup from data', (_name, text) => {
    expect(text).not.toContain('dangerouslySetInnerHTML');
    expect(text).not.toContain('innerHTML');
  });

  it('reads only with GET', () => {
    const calls = files.flatMap(([, text]) => text.match(/api\.(GET|POST|PUT|PATCH|DELETE)\(/g) ?? []);
    expect(calls.length).toBeGreaterThan(3);
    expect(calls.every((c) => c.startsWith('api.GET'))).toBe(true);
  });
});
