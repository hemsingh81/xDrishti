// Copies the browser builds of the chart libraries and the Inter font into docs/prototype/assets/vendor (offline use).
import fs from 'node:fs';
import path from 'node:path';
const here = import.meta.dirname;
const out = path.resolve(here, '../../docs/prototype/assets/vendor');
fs.mkdirSync(out, { recursive: true });
const files = [
  ['echarts/dist/echarts.min.js', 'echarts.min.js'],
  ['lightweight-charts/dist/lightweight-charts.standalone.production.js', 'lightweight-charts.js'],
  ['@fontsource-variable/inter/files/inter-latin-wght-normal.woff2', 'inter-latin-wght-normal.woff2'],
];
for (const [src, dst] of files) fs.copyFileSync(path.join(here, 'node_modules', src), path.join(out, dst));
console.log('Vendored:', files.map((f) => f[1]).join(', '));
