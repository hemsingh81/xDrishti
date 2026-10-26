import { describe, expect, it } from 'vitest';
import { findWrites } from './writeScan';

// P8-18 criterion 15: the one place that says which features may write to the server. A feature in neither list is
// read-only, so a new feature that writes fails here until someone adds it to WRITER_FEATURES on purpose.
const WRITER_FEATURES = new Set([
  'accounts',
  'auth',
  'baskets',
  'data',
  'instruments',
  'lab',
  'owner-acts',
  'plans',
  'portfolio',
  'quality',
  'services',
  'settings',
]);

const sources = import.meta.glob<string>(
  [
    '../features/**/*.{ts,tsx}',
    '!../features/**/*.test.{ts,tsx}',
    '!../features/**/testing.{ts,tsx}',
    '!../features/**/*.stories.tsx',
  ],
  { query: '?raw', import: 'default', eager: true },
);

const byFeature = new Map<string, [string, string][]>();
for (const [path, text] of Object.entries(sources)) {
  const feature = path.split('/')[2] ?? '';
  byFeature.set(feature, [...(byFeature.get(feature) ?? []), [path, text]]);
}

describe('the scanner sees a planted write', () => {
  it.each([
    ["fetch('/api/x', { method: 'post' })"],
    ['fetch(url, { method: verb })'],
    ["navigator.sendBeacon('/api/x')"],
    ['const save = useMutation({ mutationFn })'],
  ])('flags %s', (text) => {
    expect(findWrites(text)).not.toEqual([]);
  });
});

describe('write paths are only in the features that may write', () => {
  it('finds the feature sources', () => {
    expect(byFeature.size).toBeGreaterThan(10);
    expect(byFeature.has('learning')).toBe(true);
  });

  it.each([...byFeature.entries()].filter(([feature]) => !WRITER_FEATURES.has(feature)))(
    '%s is read only',
    (_feature, files) => {
      const findings = files.map(([path, text]) => ({ path, found: findWrites(text) })).filter((x) => x.found.length);
      expect(findings).toEqual([]);
    },
  );

  it('lists only features that exist or are planned', () => {
    const unknown = [...WRITER_FEATURES].filter((name) => !byFeature.has(name));
    expect(unknown).toEqual([]);
  });
});
