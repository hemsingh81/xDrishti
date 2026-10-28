import { describe, expect, it } from 'vitest';
import { findWrites } from '@/test/writeScan';

// Spec P11-05 criterion 9: the Compliance tab calls no broker and its form is unreachable by a read-only test. The one write of the feature is in the dialog file;
// no other file of the feature has a mutation, a write call or an import of another feature's code.
const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

describe('the live feature writes in one file only', () => {
  const files = Object.entries(sources);

  it('finds its own sources', () => {
    expect(files.length).toBeGreaterThan(6);
  });

  it('has the three owner writes in their dialogs and nowhere else', () => {
    const writers = files.filter(([, text]) => findWrites(text).length > 0).map(([name]) => name);
    expect(writers).toEqual([
      './components/ComplianceDialog.tsx',
      './components/LiveSettingsDialog.tsx',
      './components/RampDialog.tsx',
    ]);
    const dialog = sources['./components/ComplianceDialog.tsx'] ?? '';
    expect(dialog).toMatch(/api\.POST\(\s*'\/api\/live-trading\/compliance'/);
    expect(dialog.match(/api\.(POST|PUT|PATCH|DELETE)\(/g)).toHaveLength(1);
    expect(sources['./components/RampDialog.tsx'] ?? '').toMatch(/api\.POST\(\s*'\/api\/live-trading\/ramp\/steps'/);
    expect(sources['./components/LiveSettingsDialog.tsx'] ?? '').toMatch(
      /api\.PUT\(\s*'\/api\/live-trading\/settings'/,
    );
  });

  it.each(files)('%s names no broker, no order route and no external fetch', (_name, text) => {
    expect(text).not.toMatch(/dhan\.co|api\.dhan|\/api\/orders|\/orders\b|window\.open|target="_blank"|\bfetch\(/i);
  });

  it('loads the dialog on demand from the tab, so the read-only tab chunk carries no write', () => {
    const tab = sources['./components/ComplianceTab.tsx'] ?? '';
    expect(tab).toMatch(/lazy\(\(\) => import\('\.\/ComplianceDialog'\)/);
    expect(tab).not.toMatch(/useMutation|api\.(POST|PUT)/);
    const ramp = sources['./components/RampTab.tsx'] ?? '';
    expect(ramp).toMatch(/lazy\(\(\) => import\('\.\/RampDialog'\)/);
    expect(ramp).toMatch(/lazy\(\(\) => import\('\.\/LiveSettingsDialog'\)/);
    expect(ramp).not.toMatch(/useMutation|api\.(POST|PUT)/);
  });
});
