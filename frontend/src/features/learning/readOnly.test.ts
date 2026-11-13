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
// CF-12: and the lazy password dialog of the execution feature, drawn only by KillActDialog.tsx.
// CF-10 10d-2b: and the two lazy loaders of the owner acts of the Models tab (ActDialogs.tsx draws them; the write is in owner-acts).
const allowedImports =
  /^import \{ useCapability \} from '@\/features\/owner-acts';$|^import \{ BriefingCard \} from '@\/features\/briefing';$|^import \{ loadApplyRollbackDialog, loadHoldOutScoreDialog, loadMlSwitchDialog \} from '@\/features\/owner-acts';$|^import \{ PasswordReasonDialog \} from '@\/features\/execution';$/gm;

/** The text with the one allowed import taken out, then asked for any other feature import. */
const otherFeatureImports = (text: string): string[] =>
  text.replace(allowedImports, '').match(/@\/features\/[^'"]*/g) ?? [];

describe('the allow-list holds exactly one import', () => {
  it('lets the switch hook through', () => {
    expect(otherFeatureImports("import { useCapability } from '@/features/owner-acts';")).toEqual([]);
  });

  it('lets the three Models-tab act loaders through', () => {
    expect(
      otherFeatureImports(
        "import { loadApplyRollbackDialog, loadHoldOutScoreDialog, loadMlSwitchDialog } from '@/features/owner-acts';",
      ),
    ).toEqual([]);
  });

  it.each([
    "import { loadMovePage, loadMlSwitchDialog } from '@/features/owner-acts';",
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

// CF-12: the one write file of the feature is the kill acts (dismiss, apply, re-enable, automatic setting); only its dialog uses it.
const ownerActs = './api/killActsApi.ts';
const dialog = './components/KillActDialog.tsx';

describe('the Learning feature is read only except the kill acts', () => {
  const all = Object.entries(sources);
  const files = all.filter(([name]) => name !== ownerActs);

  it('has writes in the kill-acts file only, used by its dialog only', () => {
    expect(all.filter(([, text]) => findWrites(text).length > 0).map(([name]) => name)).toEqual([ownerActs]);
    expect(
      all
        .filter(([, text]) => text.includes('useKillAct'))
        .map(([name]) => name)
        .sort(),
    ).toEqual([ownerActs, dialog].sort());
  });

  it('writes exactly the four owner-act routes, and no other', () => {
    const text = all.find(([name]) => name === ownerActs)?.[1] ?? '';
    const routes = [...text.matchAll(/api\.(POST|PUT|PATCH|DELETE)\(\s*'([^']+)'/g)].map(
      (m) => `${m[1] ?? ''} ${m[2] ?? ''}`,
    );
    expect(routes.sort()).toEqual(
      [
        'POST /api/confidence/kill-proposals/{id}/apply',
        'POST /api/confidence/kill-proposals/{id}/dismiss',
        'POST /api/learning/cells/{key}/re-enable',
        'PUT /api/confidence/kill-settings',
      ].sort(),
    );
  });

  it('imports the password dialog in the kill dialog only', () => {
    expect(all.filter(([, text]) => text.includes('features/execution')).map(([name]) => name)).toEqual([dialog]);
  });

  it('finds its own sources', () => {
    expect(files.length).toBeGreaterThan(10);
    expect(files.some(([name]) => name.endsWith('learningQueries.ts'))).toBe(true);
  });

  it.each(files)('%s has no write call and no mutation', (_name, text) => {
    expect(findWrites(text)).toEqual([]);
    expect(otherFeatureImports(text)).toEqual([]);
  });
});
