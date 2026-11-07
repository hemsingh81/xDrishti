import { describe, expect, it } from 'vitest';
import { findWrites } from '@/test/writeScan';

// CF-11 part 2b: the screen is a read, except the owner's enforce switch. The feature's own sources (not its tests) may write
// only from `api/enforceApi.ts`, only the switch card may use that hook, and the one allowed import of another feature is
// the lazy password dialog of the execution feature.
const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}', '!./testing.tsx'], {
  query: '?raw',
  import: 'default',
  eager: true,
});
const files = Object.entries(sources);

const ownerAct = './api/enforceApi.ts';
const card = './components/EnforceCard.tsx';
const allowedImport = /^import \{ PasswordReasonDialog \} from '@\/features\/execution';$/gm;

describe('the Readiness feature writes only through the owner act', () => {
  it('finds its own sources', () => {
    expect(files.length).toBeGreaterThan(8);
    expect(files.some(([name]) => name === ownerAct)).toBe(true);
  });

  it('has a write call in the owner-act file only', () => {
    const writers = files.filter(([, text]) => findWrites(text).length > 0).map(([name]) => name);
    expect(writers).toEqual([ownerAct]);
  });

  it('names the switch route for writing in that file only', () => {
    const holders = files
      .filter(([, text]) => /\.PUT\(\s*'\/api\/confidence\/readiness-settings'/.test(text))
      .map(([name]) => name);
    expect(holders).toEqual([ownerAct]);
  });

  it('lets only the switch card use the mutation', () => {
    const users = files.filter(([, text]) => text.includes('useChangeEnforce')).map(([name]) => name);
    expect(users.sort()).toEqual([ownerAct, card].sort());
  });

  it.each(files)('%s imports no other feature than the password dialog', (_name, text) => {
    expect(text.replace(allowedImport, '').match(/@\/features\/[^'"]*/g) ?? []).toEqual([]);
  });
});
