import { describe, expect, it } from 'vitest';

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

  it.each(files)('%s has no write call and no mutation', (_name, text) => {
    expect(text).not.toMatch(/api\.(POST|PUT|PATCH|DELETE)\b/);
    expect(text).not.toMatch(/\buseMutation\b|\bensureOk\b|\bmethod:\s*['"](POST|PUT|PATCH|DELETE)/);
    expect(text).not.toMatch(/@\/features\//);
  });
});
