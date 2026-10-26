import { describe, expect, it } from 'vitest';
import { findWrites } from './writeScan';

// P8-18 criterion 15: the one place that says which features may write to the server. A feature in neither list is
// read-only, so a new feature that writes fails here until someone adds it to WRITER_FEATURES on purpose.
const WRITER_FEATURES = new Set([
  'accounts',
  'auth',
  'baskets',
  'data',
  'exports',
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

// P8-19: the six owner-act writes of the learning forms are named in one feature only (the generated schema and the shared client's password-path list are outside `features`).
const moveRoutes = [
  '/api/lessons/{id}/link',
  '/api/lessons/{id}/close',
  '/api/lessons/{id}/dismiss',
  '/api/hypotheses/{id}/move',
  '/api/changelog/{id}/revert',
  '/api/autonomy/settings',
];

describe('the export route is written from exports only', () => {
  it('names the route in no other feature', () => {
    const holders = [...byFeature.entries()]
      .filter(([, files]) => files.some(([, text]) => /\.POST\(\s*'\/api\/exports\/\{kind\}'/.test(text)))
      .map(([feature]) => feature)
      .sort();
    expect(holders).toEqual(['exports']);
  });
});

describe('the learning move routes are written from owner-acts only', () => {
  const writes = (route: string, text: string) =>
    new RegExp(`\\.(POST|PUT)\\(\\s*'${route.replace(/[{}/]/g, (c) => `\\${c}`)}'`).test(text);
  const holders = (route: string) =>
    [...byFeature.entries()]
      .filter(([, files]) => files.some(([, text]) => writes(route, text)))
      .map(([feature]) => feature)
      .sort();

  it.each(moveRoutes)('%s', (route) => {
    expect(holders(route)).toEqual(['owner-acts']);
  });

  it('the scan sees a planted write and ignores a read', () => {
    expect(writes('/api/lessons/{id}/link', "api.POST('/api/lessons/{id}/link', {})")).toBe(true);
    expect(writes('/api/autonomy/settings', "api.PUT(\n '/api/autonomy/settings', {})")).toBe(true);
    expect(writes('/api/autonomy/settings', "api.GET('/api/autonomy/settings', {})")).toBe(false);
  });
});
