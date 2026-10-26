import { rmSync } from 'node:fs';
import { matrixFile } from './record';

/** A full run starts a fresh results file; a run narrowed with --grep or a file keeps it and adds (the recorder keeps the newest line). */
export default function globalSetup(): void {
  const narrowed = process.argv.some((a) => a === '--grep' || a.startsWith('--grep=') || a.endsWith('.spec.ts'));
  if (!narrowed) rmSync(matrixFile, { force: true });
}
