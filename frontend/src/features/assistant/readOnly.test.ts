import { describe, expect, it } from 'vitest';

// AC 13: the feature reads (GET on /api/assistant/*), asks (POST /api/assistant/chat) and changes the settings (PUT
// /api/assistant/settings). Nothing else, and nothing is kept in the browser.
const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}', '!./testing.ts'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

describe('assistant feature is read-only apart from asking and the gated settings change', () => {
  const all = Object.entries(sources);

  it('finds its sources', () => {
    expect(all.length).toBeGreaterThan(10);
  });

  it('calls only the allowed routes with the allowed verbs', () => {
    const calls = all.flatMap(([path, text]) =>
      [...text.matchAll(/\bapi\.(GET|POST|PUT|PATCH|DELETE)\(\s*'([^']+)'/g)].map((m) => `${m[1]} ${m[2]} (${path})`),
    );
    const allowed =
      /^(GET \/api\/assistant\/(status|conversations|conversations\/\{id\}|tool-calls\/\{id\})|POST \/api\/assistant\/chat|PUT \/api\/assistant\/settings) /;
    expect(calls.length).toBeGreaterThan(4);
    expect(calls.filter((c) => !allowed.test(c))).toEqual([]);
  });

  it('uses no raw fetch, no browser storage and no HTML injection', () => {
    for (const [path, text] of all) {
      expect(text, path).not.toMatch(/\bfetch\s*\(/);
      expect(text, path).not.toMatch(/localStorage|sessionStorage|indexedDB/);
      expect(text, path).not.toMatch(/dangerouslySetInnerHTML|innerHTML/);
    }
  });
});
