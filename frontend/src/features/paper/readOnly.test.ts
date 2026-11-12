import { describe, expect, it } from 'vitest';
import { findWrites, withoutShadowSwitch } from '@/test/writeScan';

// The Paper screen is read only: its sources contain no mutation, no call that is not a GET, no import of another feature.
const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

describe('the Paper feature is read only', () => {
  const files = Object.entries(sources);

  it('finds its own sources', () => {
    expect(files.some(([name]) => name.endsWith('paperQueries.ts'))).toBe(true);
  });

  it.each(files)('%s has no write call and no mutation', (name, text) => {
    // OPS-01 (ADR 0278): shadowQueries.ts may hold exactly the one PUT of the owner's shadow switch and its one useMutation.
    expect(findWrites(withoutShadowSwitch(name.replace('./', '../features/paper/'), text))).toEqual([]);
    expect(text).not.toMatch(/@\/features\//);
  });
});
