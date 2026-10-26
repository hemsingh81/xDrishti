// P8-16 results recorder: `node scripts/p8Checklist.mjs [file]`. Reads data/p8-evidence/browser.jsonl (one JSON
// line per check, appended by the runner), keeps the newest line per screen/width/scheme/state/check, rewrites the file
// sorted, and writes browser-checklist-summary.md (counts, then the failed checks per screen and width). Exits 1 when a
// performance check (C14) is above twice its budget; every other failure is a finding, not a script error.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = process.env.XD_EVIDENCE_DIR
  ? resolve(process.env.XD_EVIDENCE_DIR)
  : resolve(dirname(fileURLToPath(import.meta.url)), '../../data/p8-evidence');
const file = process.argv[2] ? resolve(process.argv[2]) : join(root, 'browser.jsonl');
if (!existsSync(file)) {
  console.error(`No results at ${file}: run \`npm run test:browser\` first.`);
  process.exit(1);
}

const lines = readFileSync(file, 'utf8').split('\n').filter(Boolean);
const newest = new Map();
for (const line of lines) {
  const row = JSON.parse(line);
  const key = [row.screen, row.width, row.scheme, row.state, row.check].join('|');
  const old = newest.get(key);
  if (!old || old.date <= row.date) newest.set(key, row);
}
const rows = [...newest.values()].sort((a, b) =>
  [a.screen, a.width, a.scheme, a.state, a.check]
    .join('|')
    .localeCompare([b.screen, b.width, b.scheme, b.state, b.check].join('|'), 'en', { numeric: true }),
);
writeFileSync(file, rows.map((r) => JSON.stringify(r)).join('\n') + '\n');

const count = (result) => rows.filter((r) => r.result === result).length;
const failed = rows.filter((r) => r.result === 'fail');
const byCheck = new Map();
for (const r of failed) byCheck.set(r.check, (byCheck.get(r.check) ?? 0) + 1);
const perScreen = new Map();
for (const r of failed) {
  const key = `${r.screen} @ ${r.width} ${r.scheme}`;
  perScreen.set(key, [...(perScreen.get(key) ?? []), r]);
}
const commits = [...new Set(rows.map((r) => r.commit))];
const commit = commits.join(', ') || 'unknown';
const out = [
  '# Browser checklist summary (P8-16)',
  '',
  `${rows.length} check results (commit ${commit}): ${count('pass')} pass, ${count('fail')} fail, ${count('na')} na.`,
  '',
  ...(commits.length > 1
    ? [
        `WARNING: ${commits.length} commits are present in the rows; a full \`npm run test:browser\` run starts a fresh file.`,
        '',
      ]
    : []),
  '## Failures by check',
  '',
  ...[...byCheck].sort((a, b) => b[1] - a[1]).map(([check, n]) => `- ${check}: ${n}`),
  '',
  '## Failed checks per screen and width',
  '',
  ...[...perScreen].flatMap(([key, items]) => [
    `### ${key}`,
    ...items.map((r) => `- ${r.state} ${r.check}: ${r.note.slice(0, 220)}`),
    '',
  ]),
  '## Performance (C14)',
  '',
  ...rows.filter((r) => r.check === 'C14').map((r) => `- ${r.screen} @ ${r.width}: ${r.note}`),
  '',
];
writeFileSync(join(root, 'browser-checklist-summary.md'), out.join('\n'));
if (commits.length > 1)
  console.warn(`p8Checklist: rows from ${commits.length} commits (${commit}); rerun the full matrix`);
console.log(`p8Checklist: ${rows.length} results, ${count('pass')} pass, ${count('fail')} fail, ${count('na')} na`);
process.exit(rows.some((r) => r.check === 'C14' && r.result === 'fail') ? 1 : 0);
