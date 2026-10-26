import { describe, expect, it } from 'vitest';
import { findWrites } from '@/test/writeScan';

// P8-00 D6: a read-only screen has no way to write. The feature's own sources (not its tests) must contain no mutation, no
// call that is not a GET, and no import of another feature.
const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

// P8-20: the one allowed import of another feature: the export control. The writing is in `features/exports`, not here.
const allowedImports = /^import \{ ExportMenu \} from '@\/features\/exports';$/m;

/** The text with the one allowed import taken out, then asked for any other feature import. */
const otherFeatureImports = (text: string): string[] =>
  text.replace(allowedImports, '').match(/@\/features\/[^'"]*/g) ?? [];

describe('the allow-list holds exactly one import', () => {
  it('lets the export control through', () => {
    expect(otherFeatureImports("import { ExportMenu } from '@/features/exports';")).toEqual([]);
  });

  it.each([
    "import { useExport } from '@/features/exports';",
    "import { ExportMenu, useExport } from '@/features/exports';",
    "import { ExportMenu } from '@/features/exports/components/ExportMenu';",
    "import { OwnerActsPage } from '@/features/owner-acts';",
    "import { ExportMenu } from '@/features/lab';",
  ])('refuses %s', (text) => {
    expect(otherFeatureImports(text)).not.toEqual([]);
  });

  it('still finds a write call', () => {
    expect(findWrites("api.POST('/api/exports/{kind}', {})")).not.toEqual([]);
  });
});

describe('the Reports feature is read only', () => {
  const files = Object.entries(sources);

  it('finds its own sources', () => {
    expect(files.length).toBeGreaterThan(10);
    expect(files.some(([name]) => name.endsWith('reportQueries.ts'))).toBe(true);
  });

  it.each(files)('%s has no write call and no mutation', (_name, text) => {
    expect(findWrites(text)).toEqual([]);
    expect(otherFeatureImports(text)).toEqual([]);
  });
});
