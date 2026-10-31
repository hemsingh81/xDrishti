import { describe, expect, it } from 'vitest';
import { isAllowedWrite } from './testing';

const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}', '!./testing.ts'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

describe('the Execution screens write only to endpoints that already exist (P12-10 criterion 15)', () => {
  it('accepts every listed write and refuses any other non-GET', () => {
    expect(isAllowedWrite('POST', '/api/execution/halts')).toBe(true);
    expect(isAllowedWrite('POST', '/api/execution/halts/17/release')).toBe(true);
    expect(isAllowedWrite('PUT', '/api/execution/limits/abc')).toBe(true);
    expect(isAllowedWrite('GET', '/api/execution/orders')).toBe(true);
    expect(isAllowedWrite('POST', '/api/execution/orders')).toBe(false);
    expect(isAllowedWrite('DELETE', '/api/execution/halts/17')).toBe(false);
    expect(isAllowedWrite('PUT', '/api/execution/modes/abc')).toBe(false);
  });

  it('finds no write call in the feature source outside the list', () => {
    const calls = Object.values(sources).flatMap((text) =>
      [...text.matchAll(/api\.(POST|PUT|DELETE|PATCH)\(\s*'([^']+)'/g)].map((m) => ({
        method: m[1] ?? '',
        path: (m[2] ?? '').replace(/\{[^}]+\}/g, 'x'),
      })),
    );
    expect(calls.length).toBeGreaterThan(8);
    expect(calls.filter((c) => !isAllowedWrite(c.method, c.path))).toEqual([]);
  });
});
