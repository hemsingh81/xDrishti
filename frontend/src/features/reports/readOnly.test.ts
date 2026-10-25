import { describe, expect, it } from 'vitest';

// P8-00 D6: a read-only screen has no way to write. The feature's own sources (not its tests) must contain no mutation, no
// call that is not a GET, and no import of another feature.
const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

describe('the Reports feature is read only', () => {
  const files = Object.entries(sources);

  it('finds its own sources', () => {
    expect(files.length).toBeGreaterThan(10);
    expect(files.some(([name]) => name.endsWith('reportQueries.ts'))).toBe(true);
  });

  it.each(files)('%s has no write call and no mutation', (_name, text) => {
    expect(text).not.toMatch(/api\.(POST|PUT|PATCH|DELETE)\b/);
    expect(text).not.toMatch(/\buseMutation\b|\bensureOk\b|\bmethod:\s*['"](POST|PUT|PATCH|DELETE)/);
    expect(text).not.toMatch(/@\/features\//);
  });
});
