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

  it.each(files)('%s has no write call and no mutation', (name, text) => {
    // OPS-01 (ADR 0278): shadowQueries.ts may hold exactly the one PUT of the owner's shadow switch, and nothing else.
    const isShadow = name.endsWith('shadowQueries.ts');
    const body = isShadow ? text.replace("api.PUT('/api/paper/shadow/settings'", '') : text;
    expect(body).not.toMatch(/api\.(POST|PUT|PATCH|DELETE)\b/);
    if (!isShadow) expect(text).not.toMatch(/\buseMutation\b|\bensureOk\b|\bmethod:\s*['"](POST|PUT|PATCH|DELETE)/);
    expect(text).not.toMatch(/@\/features\//);
  });
});
