// `npm run size`: the bundle guard. Reads dist/ (run `npm run build` first), prints the first-load JavaScript (entry +
// modulepreload chunks, gzip) and the biggest lazy chunks, and exits 1 above the 200 KB budget (frontend/CLAUDE.md).
// `node scripts/size.mjs <dist dir>` measures another build (the guard is exercised on a planted import that way).
import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { firstLoadAssets, gzipBytes, kb, measureFirstLoad, report } from './firstLoad.mjs';

const dist = process.argv[2]
  ? resolve(process.argv[2])
  : resolve(fileURLToPath(new URL('..', import.meta.url)), 'dist');
const read = (href) => readFileSync(join(dist, href.replace(/^\//, '')));

let html;
let result;
try {
  html = readFileSync(join(dist, 'index.html'), 'utf8');
  result = measureFirstLoad(html, read);
} catch (error) {
  console.error(`Bundle guard FAILED: ${error instanceof Error ? error.message : String(error)}`);
  console.error('Run `npm run build` first.');
  process.exit(1);
}

console.log(report(result).join('\n'));

// Information only: the largest chunks that load on demand (a route, the chart, the editor).
const eager = new Set(firstLoadAssets(html).map((href) => href.replace(/^\//, '')));
const lazy = readdirSync(join(dist, 'assets'))
  .filter((name) => name.endsWith('.js') && !eager.has(`assets/${name}`))
  .map((name) => ({ name, gzip: gzipBytes(readFileSync(join(dist, 'assets', name))) }))
  .sort((a, b) => b.gzip - a.gzip)
  .slice(0, 5);
console.log('Largest lazy chunks (not counted):');
for (const c of lazy) console.log(`  ${kb(c.gzip).padStart(9)}  /assets/${c.name}`);

if (!result.withinBudget) {
  console.error('Bundle guard FAILED: the first load is over budget.');
  process.exit(1);
}
