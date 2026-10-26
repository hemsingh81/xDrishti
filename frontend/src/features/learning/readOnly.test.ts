import { describe, expect, it } from 'vitest';
import { findWrites } from '@/test/writeScan';

// P8-00 D6: a read-only screen has no way to write. The feature's own sources (not its tests) must contain no mutation, no
// call that is not a GET, and no import of another feature.
const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}', '!./testing.tsx'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

describe('the Learning feature is read only', () => {
  const files = Object.entries(sources);

  it('finds its own sources', () => {
    expect(files.length).toBeGreaterThan(10);
    expect(files.some(([name]) => name.endsWith('learningQueries.ts'))).toBe(true);
  });

  it.each(files)('%s has no write call and no mutation', (_name, text) => {
    expect(findWrites(text)).toEqual([]);
    expect(text).not.toMatch(/@\/features\//);
  });
});
