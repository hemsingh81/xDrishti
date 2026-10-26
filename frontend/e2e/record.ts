import { execFileSync } from 'node:child_process';
import { appendFileSync, mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Ctx, Result } from './checks/types';

export const evidenceDir = fileURLToPath(new URL('../../data/p8-evidence/', import.meta.url));
export const shotsDir = `${evidenceDir}shots/`;

mkdirSync(shotsDir, { recursive: true });

const commit = (() => {
  try {
    return execFileSync('git', ['rev-parse', '--short', 'HEAD'], { encoding: 'utf8' }).trim();
  } catch {
    return 'unknown';
  }
})();

/** The self-test passes its own file, never the matrix one. */
export const matrixFile = `${evidenceDir}browser.jsonl`;

/** Appends one JSON line per check (small appends are atomic enough across the workers; the recorder script de-duplicates). */
export function record(ctx: Ctx, results: Result[], file = matrixFile): void {
  mkdirSync(dirname(file), { recursive: true });
  const date = new Date().toISOString();
  const lines = results.map((r) =>
    JSON.stringify({ ...ctx, check: r.check, result: r.status, note: r.note, commit, date }),
  );
  appendFileSync(file, `${lines.join('\n')}\n`);
}
