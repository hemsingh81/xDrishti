import { describe, expect, it } from 'vitest';
import { findWrites } from '@/test/writeScan';

// P8-00 D6: a read-only screen has no way to write. The feature's own sources (not its tests) must contain no mutation, no
// call that is not a GET, and no import of another feature.
const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}', '!./testing.tsx'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

// P8-18: the one allowed import of another feature: the switch hook, to draw a plain link to the replay screen.
// P9-06: and the read-only briefing card, to show the week's review text beside the digest.
const allowedImports =
  /^import \{ useCapability \} from '@\/features\/owner-acts';$|^import \{ BriefingCard \} from '@\/features\/briefing';$/gm;

/** The text with the one allowed import taken out, then asked for any other feature import. */
const otherFeatureImports = (text: string): string[] =>
  text.replace(allowedImports, '').match(/@\/features\/[^'"]*/g) ?? [];

describe('the allow-list holds exactly one import', () => {
  it('lets the switch hook through', () => {
    expect(otherFeatureImports("import { useCapability } from '@/features/owner-acts';")).toEqual([]);
  });

  it.each([
    "import { OwnerActsPage } from '@/features/owner-acts';",
    "import { useCapability, loadExperimentPage } from '@/features/owner-acts';",
    "import { useCapability } from '@/features/owner-acts/api/capabilitiesApi';",
    "import { ExportMenu } from '@/features/exports';",
    "import { useCapability } from '@/features/lab';",
  ])('refuses %s', (text) => {
    expect(otherFeatureImports(text)).not.toEqual([]);
  });

  it('still finds a write call', () => {
    expect(findWrites("api.POST('/api/experiments', {})")).not.toEqual([]);
  });
});

describe('the Learning feature is read only', () => {
  const files = Object.entries(sources);

  it('finds its own sources', () => {
    expect(files.length).toBeGreaterThan(10);
    expect(files.some(([name]) => name.endsWith('learningQueries.ts'))).toBe(true);
  });

  it.each(files)('%s has no write call and no mutation', (_name, text) => {
    expect(findWrites(text)).toEqual([]);
    expect(otherFeatureImports(text)).toEqual([]);
  });
});
