import { describe, expect, it } from 'vitest';

// The audit screen is read only (spec P8-22): no write call, no mutation, no export or copy-all, no import of another feature.
const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

describe('the Audit feature is read only', () => {
  const files = Object.entries(sources);

  it('finds its own sources', () => {
    expect(files.length).toBeGreaterThan(5);
  });

  it.each(files)('%s has no write call, no export and no cross-feature import', (_name, text) => {
    expect(text).not.toMatch(/api\.(POST|PUT|PATCH|DELETE)\b/);
    expect(text).not.toMatch(/\buseMutation\b|\bensureOk\b|\bmethod:\s*['"](POST|PUT|PATCH|DELETE)/);
    expect(text).not.toMatch(/@\/features\//);
    expect(text).not.toMatch(/\bBlob\b|createObjectURL|clipboard|download=/);
  });
});
